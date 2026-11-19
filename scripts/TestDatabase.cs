#:property PublishAot=false

using System.Diagnostics;

// starts or stops a disposable PostgreSQL container for the relational tests. local only, ci uses its own service container.
// usage: dotnet run --file scripts/TestDatabase.cs -- up | down | env
// runtime: TRAVELTRACKER_CONTAINER_RUNTIME=podman|docker, otherwise podman if installed, else docker.
const string usage = "dotnet run --file scripts/TestDatabase.cs -- up | down | env";
const string containerName = "traveltracker-test-pg";
const string image = "docker.io/library/postgres:17";
const int port = 54329;
var connectionString = $"Host=localhost;Port={port};Username=postgres;Password=test;Database=postgres";

if (args.Length != 1 || args[0] is not ("up" or "down" or "env"))
{
    Console.Error.WriteLine(usage);
    return 2;
}

if (args[0] == "env")
{
    Console.WriteLine(connectionString);
    return 0;
}

var runtime = ResolveRuntime();
if (runtime is null)
{
    Console.Error.WriteLine("ERROR no container runtime found: install podman or docker, or set TRAVELTRACKER_CONTAINER_RUNTIME.");
    return 1;
}

Console.WriteLine($"INFO using {runtime}");
if (args[0] == "down")
{
    var removed = Run(runtime, "rm", "-f", containerName);
    Console.WriteLine(removed.Code == 0 ? $"INFO removed {containerName}" : $"WARN could not remove {containerName}: {removed.Output}");
    return 0;
}

var existing = Run(runtime, "inspect", "--format", "{{.State.Running}}", containerName);
if (existing.Code == 0 && existing.Output == "true") Console.WriteLine($"INFO {containerName} is already running");
else
{
    if (existing.Code == 0) Run(runtime, "rm", "-f", containerName);
    var started = Run(runtime, "run", "-d", "--name", containerName, "-e", "POSTGRES_PASSWORD=test", "-p", $"127.0.0.1:{port}:5432", image);
    if (started.Code != 0)
    {
        Console.Error.WriteLine($"ERROR could not start {containerName}: {started.Output}");
        return 1;
    }
}

for (var attempt = 0; attempt < 30; attempt++)
{
    if (Run(runtime, "exec", containerName, "pg_isready", "-U", "postgres").Code == 0)
    {
        Console.WriteLine("INFO postgres is accepting connections");
        Console.WriteLine($"TRAVELTRACKER_TEST_POSTGRES=\"{connectionString}\"");
        return 0;
    }
    Thread.Sleep(1000);
}

Console.Error.WriteLine($"ERROR postgres did not become ready in 30 seconds; check '{runtime} logs {containerName}'");
return 1;

string? ResolveRuntime()
{
    var requested = Environment.GetEnvironmentVariable("TRAVELTRACKER_CONTAINER_RUNTIME");
    if (requested is { Length: > 0 }) return Run(requested, "--version").Code == 0 ? requested : null;
    return new[] { "podman", "docker" }.FirstOrDefault(candidate => Run(candidate, "--version").Code == 0);
}

(int Code, string Output) Run(string file, params string[] arguments)
{
    var startInfo = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    try
    {
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, (output.Result + error.Result).Trim());
    }
    catch (System.ComponentModel.Win32Exception exception)
    {
        return (127, exception.Message);
    }
}
