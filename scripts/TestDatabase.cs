#:property PublishAot=false

using System.Diagnostics;
using System.Net.Sockets;

// runs the tests against a disposable postgres that lives only as long as this script.
// it reuses scripts/StartLocalDb.cs as a child (same idea as StartDev.cs --local-db), points TRAVELTRACKER_TEST_POSTGRES at it,
// runs dotnet test, then closes the child's stdin so the container is removed even when the tests fail.
// usage: dotnet run --file scripts/TestDatabase.cs [-- <dotnet test arguments>]   (default: TravelTracker.Tests.slnx)
// runtime selection (podman, then docker) lives in StartLocalDb.cs: TRAVELTRACKER_CONTAINER_RUNTIME overrides it.
const string marker = "TRAVELTRACKER_DATABASE=";
const int databasePort = 54330;
var testArguments = args.Length > 0 ? args : ["TravelTracker.Tests.slnx"];

if (!File.Exists(Path.Combine("scripts", "StartLocalDb.cs")))
{
    Console.Error.WriteLine("ERROR run this from the TravelTracker repository root; scripts/StartLocalDb.cs was not found.");
    return 1;
}

if (PortInUse(databasePort))
{
    // StartLocalDb removes its container on exit, so sharing one already running would delete it under its owner.
    Console.Error.WriteLine($"ERROR port {databasePort} is already in use, probably by StartLocalDb.cs. Stop it first, or set TRAVELTRACKER_TEST_POSTGRES yourself and run dotnet test directly.");
    return 1;
}

var databaseStart = new ProcessStartInfo("dotnet") { RedirectStandardInput = true, RedirectStandardOutput = true };
foreach (var argument in new[] { "run", "--file", Path.Combine("scripts", "StartLocalDb.cs"), "--", "--follow-parent" }) databaseStart.ArgumentList.Add(argument);
using var database = Process.Start(databaseStart) ?? throw new InvalidOperationException("Could not start the local database script.");

// ctrl+c reaches the whole process group; keep going here so the cleanup below still runs
Console.CancelKeyPress += (_, signal) => signal.Cancel = true;

var exitCode = 1;
try
{
    var connection = await ReadConnectionAsync(database);
    if (connection is null)
    {
        Console.Error.WriteLine("ERROR the local database script stopped before the database was ready. See its output above.");
        return 1;
    }

    var testStart = new ProcessStartInfo("dotnet");
    testStart.ArgumentList.Add("test");
    foreach (var argument in testArguments) testStart.ArgumentList.Add(argument);
    testStart.Environment["TRAVELTRACKER_TEST_POSTGRES"] = connection;
    Console.WriteLine($"INFO dotnet test {string.Join(' ', testArguments)}");
    using var tests = Process.Start(testStart) ?? throw new InvalidOperationException("Could not start dotnet test.");
    await tests.WaitForExitAsync();
    exitCode = tests.ExitCode;
}
finally
{
    // closing stdin is what tells StartLocalDb.cs to remove the container
    database.StandardInput.Close();
    if (!database.WaitForExit(30000))
    {
        Console.Error.WriteLine("WARN the database script did not stop in 30 seconds; killing it. Check for a leftover container named traveltracker-dev-pg.");
        database.Kill(entireProcessTree: true);
    }
}

Console.WriteLine(exitCode == 0 ? "INFO tests passed, database removed" : $"ERROR tests failed with exit code {exitCode}, database removed");
return exitCode;

static bool PortInUse(int port)
{
    try
    {
        using var client = new TcpClient();
        return client.ConnectAsync("127.0.0.1", port).Wait(500);
    }
    catch (Exception)
    {
        return false;
    }
}

static async Task<string?> ReadConnectionAsync(Process database)
{
    while (await database.StandardOutput.ReadLineAsync() is { } line)
    {
        Console.WriteLine("[db] " + line);
        if (!line.StartsWith(marker)) continue;
        // keep draining so the child never blocks on a full pipe
        _ = database.StandardOutput.ReadToEndAsync();
        return line[marker.Length..].Trim('"');
    }
    return null;
}
