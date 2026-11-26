#:property PublishAot=false

using System.Diagnostics;
using System.Text.Json;

// Offline process tests: no database, token requests, certificate trust changes, or service startup.
var repo = Directory.GetCurrentDirectory();
var temporary = Path.Combine(Path.GetTempPath(), "traveltracker-launch-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var certificate = Path.Combine(temporary, "certificate with spaces.crt");
File.WriteAllText(certificate, "offline fixture; configuration check only");
var configPath = Path.Combine(temporary, "settings.json");
var baseline = new Dictionary<string, string>
{
    ["PoolerHost"] = "aws-0-us-east-1.pooler.supabase.com",
    ["DatabaseUser"] = "runtime.exampleproject",
    ["RootCertificate"] = certificate
};
var passed = 0;
try
{
    await Check("valid saved settings and certificate path with spaces", new(baseline), "configuration is valid", 0, ["--supabase", "--check-config"]);
    await Check("first run without a terminal explains missing settings", new(), "PoolerHost is missing", 1, ["--supabase", "--check-config"]);
    await Check("direct host explains session pooler requirement", Changed("PoolerHost", "db.example.supabase.co"), "Session pooler", 1, ["--supabase", "--check-config"]);
    await Check("pooler username requires project suffix", Changed("DatabaseUser", "postgres"), "project-reference suffix", 1, ["--supabase", "--check-config"]);
    await Check("missing certificate explains local path", Changed("RootCertificate", Path.Combine(temporary, "missing.crt")), "does not exist on this machine", 1, ["--supabase", "--check-config"]);
    var obsolete = new Dictionary<string, string>(baseline) { ["SupabaseUrl"] = "https://exampleproject.supabase.co", ["Auth0Domain"] = "old.example.auth0.com" };
    await Check("obsolete auth settings are reported and dropped", obsolete, "Ignoring obsolete launch setting SupabaseUrl", 0, ["--supabase", "--setup"]);
    Require(!File.ReadAllText(configPath).Contains("SupabaseUrl") && !File.ReadAllText(configPath).Contains("Auth0Domain"), "Obsolete settings were saved again");
    var secretField = new Dictionary<string, string>(baseline) { ["Password"] = "do-not-print-this" };
    await Check("secret fields rejected without disclosure", secretField, "unsupported fields", 1, ["--supabase", "--setup"]);
    await Check("URI password discarded on save", Changed("PoolerHost", "postgresql://runtime.exampleproject:do-not-print-this@aws-0-us-east-1.pooler.supabase.com:5432/postgres"), "configuration is valid", 0, ["--supabase", "--setup"]);
    Require(!File.ReadAllText(configPath).Contains("do-not-print-this"), "URI password leaked into settings");
    var saved = JsonDocument.Parse(File.ReadAllText(configPath)).RootElement;
    Require(saved.GetProperty("PoolerHost").GetString() == baseline["PoolerHost"], "Host was not extracted");
    Require(saved.GetProperty("DatabaseUser").GetString() == "runtime.exampleproject", "URI user was not extracted");
    await Check("Supabase password placeholder URI accepted", Changed("PoolerHost", "postgresql://runtime.exampleproject:[YOUR-PASSWORD]@aws-0-us-east-1.pooler.supabase.com:5432/postgres"), "configuration is valid", 0, ["--supabase", "--setup"]);
    Require(!File.ReadAllText(configPath).Contains("YOUR-PASSWORD"), "Placeholder was saved");
    await Check("transaction pooler port rejected", Changed("PoolerHost", "postgresql://runtime.exampleproject:do-not-print-this@aws-0-us-east-1.pooler.supabase.com:6543/postgres"), "port 5432", 1, ["--supabase", "--check-config"]);

    // the same launcher against any other postgres server
    var raw = new Dictionary<string, string> { ["TRAVELTRACKER_DATABASE"] = "Host=localhost;Port=54329;Username=postgres;Password=do-not-print-this" };
    await Check("a raw connection string is valid without any saved settings", new(), "configuration is valid", 0, ["--check-config"], raw);
    await Check("a raw connection string with bad syntax is rejected", new(), "not valid Npgsql syntax", 1, ["--check-config"], new() { ["TRAVELTRACKER_DATABASE"] = "no equals sign here" });
    await Check("no database chosen explains both ways to choose one", new(), "TRAVELTRACKER_DATABASE", 1, ["--check-config"]);
    await Check("a raw connection and the supabase preset cannot both be chosen", new(baseline), "both choose the database", 1, ["--supabase", "--check-config"], raw);
    await Check("unknown flags print usage", new(), "dotnet run --file scripts/StartDev.cs", 2, ["--bogus"]);
    await File.WriteAllTextAsync(configPath, "{ invalid json");
    var malformed = await Run(["--supabase", "--check-config"]);
    Require(malformed.Code == 1 && malformed.Output.Contains("invalid JSON"), "Malformed settings need a useful error");
    Console.WriteLine("PASS malformed JSON reports recovery instructions");
    passed++;
    Console.WriteLine($"Launcher tests: {passed} passed, 0 failed.");
    return 0;
}
finally
{
    // This unique test-owned directory contains only fixtures produced above.
    Directory.Delete(temporary, recursive: true);
}

/**
 * Builds an isolated scenario with one changed setting.
 * @param key Setting to change.
 * @param value Scenario input.
 * @return A copy of the baseline with the requested change.
 */
Dictionary<string, string> Changed(string key, string value) => new(baseline) { [key] = value };

/**
 * Checks exit status, useful diagnostics, secret redaction, and check-only immutability.
 * @param name Scenario label.
 * @param config Test-owned settings.
 * @param message Required diagnostic text.
 * @param code Expected exit code.
 * @param options Launcher flags under test.
 * @param environment Extra environment for this launch.
 * @return A task completing after all assertions pass.
 */
async Task Check(string name, Dictionary<string, string> config, string message, int code, string[] options, Dictionary<string, string>? environment = null)
{
    var before = JsonSerializer.Serialize(config);
    await File.WriteAllTextAsync(configPath, before);
    var result = await Run(options, environment);
    Require(result.Code == code && result.Output.Contains(message), $"{name}: unexpected result {result.Code}: {result.Output}");
    Require(!result.Output.Contains("do-not-print-this"), name + ": secret leaked to output");
    if (options.Contains("--check-config"))
        Require(File.ReadAllText(configPath) == before, name + ": check-only modified configuration");
    Console.WriteLine("PASS " + name);
    passed++;
}

/**
 * Executes the launcher with isolated settings and closed standard input.
 * @param options Configuration-only flags to execute.
 * @param environment Extra environment for this launch.
 * @return Exit status and captured output.
 */
async Task<(int Code, string Output)> Run(string[] options, Dictionary<string, string>? environment = null)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repo, UseShellExecute = false,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var argument in new[] { "run", "--file", "scripts/StartDev.cs", "--" }.Concat(options))
        start.ArgumentList.Add(argument);
    start.Environment.Remove("TRAVELTRACKER_DATABASE");
    start.Environment["TRAVELTRACKER_LAUNCH_CONFIG"] = configPath;
    foreach (var (key, value) in environment ?? new())
        start.Environment[key] = value;
    using var process = Process.Start(start)!;
    process.StandardInput.Close();
    var output = process.StandardOutput.ReadToEndAsync();
    var error = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { process.Kill(entireProcessTree: true); throw; }
    return (process.ExitCode, await output + await error);
}

/**
 * Stops the regression script when an assertion fails.
 * @param condition Expected invariant.
 * @param message Explanation on failure.
 */
static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
