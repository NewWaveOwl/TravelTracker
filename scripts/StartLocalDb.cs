#:property PublishAot=false

using System.Diagnostics;

// starts or stops a local PostgreSQL container for development and creates the six service databases. any raw postgres works; this is just the easy one.
// usage: dotnet run --file scripts/StartLocalDb.cs -- up | down | reset | env
// runtime: TRAVELTRACKER_CONTAINER_RUNTIME=podman|docker, otherwise podman if installed, else docker.
// image: postgres:alpine because only the database runs in the container. switch to postgres:latest if the whole stack ever does.
const string usage = "dotnet run --file scripts/StartLocalDb.cs -- up | down | reset | env";
const string containerName = "traveltracker-dev-pg";
const string volumeName = "traveltracker-dev-pgdata";
const string image = "docker.io/library/postgres:alpine";
const int port = 54330;
var connectionString = $"Host=localhost;Port={port};Username=postgres;Password=dev;Maximum Pool Size=5";
var bootstrapPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "Database", "bootstrap.sql");

if (args.Length != 1 || args[0] is not ("up" or "down" or "reset" or "env"))
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
if (args[0] is "down" or "reset")
{
    var removed = Run(runtime, "rm", "-f", containerName);
    Console.WriteLine(removed.Code == 0 ? $"INFO removed {containerName}" : $"WARN could not remove {containerName}: {removed.Output}");
    if (args[0] == "down") return 0;
    var dropped = Run(runtime, "volume", "rm", "-f", volumeName);
    Console.WriteLine(dropped.Code == 0 ? $"INFO removed volume {volumeName}" : $"WARN could not remove volume {volumeName}: {dropped.Output}");
}

if (!File.Exists(bootstrapPath))
{
    Console.Error.WriteLine("ERROR run this from the TravelTracker repository root; src/Database/bootstrap.sql was not found.");
    return 1;
}

var existing = Run(runtime, "inspect", "--format", "{{.State.Running}}", containerName);
if (existing.Code == 0 && existing.Output == "true") Console.WriteLine($"INFO {containerName} is already running");
else
{
    if (existing.Code == 0) Run(runtime, "rm", "-f", containerName);
    var started = Run(runtime, "run", "-d", "--name", containerName, "-e", "POSTGRES_PASSWORD=dev",
        "-v", $"{volumeName}:/var/lib/postgresql", "-p", $"127.0.0.1:{port}:5432", image);
    if (started.Code != 0)
    {
        Console.Error.WriteLine($"ERROR could not start {containerName}: {started.Output}");
        return 1;
    }
}

var ready = false;
for (var attempt = 0; attempt < 30 && !ready; attempt++)
{
    ready = Run(runtime, "exec", containerName, "pg_isready", "-U", "postgres").Code == 0;
    if (!ready) Thread.Sleep(1000);
}

if (!ready)
{
    Console.Error.WriteLine($"ERROR postgres did not become ready in 30 seconds; check '{runtime} logs {containerName}'");
    return 1;
}

Console.WriteLine("INFO postgres is accepting connections");
var bootstrapped = RunWithInput(runtime, ["exec", "-i", containerName, "psql", "-X", "-q", "-U", "postgres", "-d", "postgres", "-f", "-"],
    File.ReadAllText(bootstrapPath));
if (bootstrapped.Code != 0)
{
    Console.Error.WriteLine($"ERROR could not create the service databases: {bootstrapped.Output}");
    return 1;
}

Console.WriteLine("INFO service databases are ready");
Console.WriteLine($"TRAVELTRACKER_DATABASE=\"{connectionString}\"");
Console.WriteLine("INFO then run: dotnet run --file scripts/StartSupabaseDev.cs -- --apply-migrations (first run only)");
return 0;

string? ResolveRuntime()
{
    var requested = Environment.GetEnvironmentVariable("TRAVELTRACKER_CONTAINER_RUNTIME");
    if (requested is { Length: > 0 }) return Run(requested, "--version").Code == 0 ? requested : null;
    return new[] { "podman", "docker" }.FirstOrDefault(candidate => Run(candidate, "--version").Code == 0);
}

(int Code, string Output) Run(string file, params string[] arguments) => RunWithInput(file, arguments, null);

(int Code, string Output) RunWithInput(string file, string[] arguments, string? standardInput)
{
    var startInfo = new ProcessStartInfo(file)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = standardInput is not null
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    try
    {
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }
        process.WaitForExit();
        return (process.ExitCode, (output.Result + error.Result).Trim());
    }
    catch (System.ComponentModel.Win32Exception exception)
    {
        return (127, exception.Message);
    }
}
