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
    ["BackendClientId"] = "test-backend-client",
    ["RootCertificate"] = certificate,
    ["Auth0Domain"] = "dev-example.us.auth0.com",
    ["FrontendClientId"] = "test-frontend-client",
    ["Audience"] = "traveltracker-api",
    ["FrontendType"] = "spa"
};
var passed = 0;
try
{
    await Check("valid saved settings and certificate path with spaces", new(baseline), "--check-config", 0, "configuration is valid");
    await Check("first run without a terminal explains missing settings", new(), "--check-config", 1, "PoolerHost is missing");
    await Check("direct host explains session pooler requirement", Changed("PoolerHost", "db.example.supabase.co"), "--check-config", 1, "Session pooler");
    await Check("pooler username requires project suffix", Changed("DatabaseUser", "postgres"), "--check-config", 1, "project-reference suffix");
    await Check("missing certificate explains local path", Changed("RootCertificate", Path.Combine(temporary, "missing.crt")), "--check-config", 1, "does not exist on this machine");
    await Check("placeholder client rejected", Changed("FrontendClientId", "YOUR-FRONTEND-CLIENT-ID"), "--check-config", 1, "example placeholder");
    await Check("domain URL normalized", Changed("Auth0Domain", "https://dev-example.us.auth0.com/"), "--setup", 0, "configuration is valid");
    Require(JsonDocument.Parse(File.ReadAllText(configPath)).RootElement.GetProperty("Auth0Domain").GetString() == "dev-example.us.auth0.com", "Domain normalization failed");
    await Check("domain with path rejected", Changed("Auth0Domain", "dev-example.us.auth0.com/path"), "--check-config", 1, "without a path");
    await Check("regular frontend type accepted", Changed("FrontendType", "regular"), "--check-config", 0, "configuration is valid");
    await Check("invalid frontend type rejected", Changed("FrontendType", "native"), "--check-config", 1, "spa or regular");
    var secretField = new Dictionary<string, string>(baseline) { ["Password"] = "do-not-print-this" };
    await Check("secret fields rejected without disclosure", secretField, "--setup", 1, "unsupported fields");
    await Check("URI password discarded on save", Changed("PoolerHost", "postgresql://runtime.exampleproject:do-not-print-this@aws-0-us-east-1.pooler.supabase.com:5432/postgres"), "--setup", 0, "configuration is valid");
    Require(!File.ReadAllText(configPath).Contains("do-not-print-this"), "URI password leaked into settings");
    var saved = JsonDocument.Parse(File.ReadAllText(configPath)).RootElement;
    Require(saved.GetProperty("PoolerHost").GetString() == baseline["PoolerHost"], "Host was not extracted");
    Require(saved.GetProperty("DatabaseUser").GetString() == "runtime.exampleproject", "URI user was not extracted");
    await Check("Supabase password placeholder URI accepted", Changed("PoolerHost", "postgresql://runtime.exampleproject:[YOUR-PASSWORD]@aws-0-us-east-1.pooler.supabase.com:5432/postgres"), "--setup", 0, "configuration is valid");
    Require(!File.ReadAllText(configPath).Contains("YOUR-PASSWORD"), "Placeholder was saved");
    await Check("transaction pooler port rejected", Changed("PoolerHost", "postgresql://runtime.exampleproject:do-not-print-this@aws-0-us-east-1.pooler.supabase.com:6543/postgres"), "--check-config", 1, "port 5432");
    await File.WriteAllTextAsync(configPath, "{ invalid json");
    var malformed = await Run("--check-config");
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
 * @param option Launcher option under test.
 * @param code Expected exit code.
 * @param message Required diagnostic text.
 * @return A task completing after all assertions pass.
 */
async Task Check(string name, Dictionary<string, string> config, string option, int code, string message)
{
    var before = JsonSerializer.Serialize(config);
    await File.WriteAllTextAsync(configPath, before);
    var result = await Run(option);
    Require(result.Code == code && result.Output.Contains(message), $"{name}: unexpected result {result.Code}: {result.Output}");
    Require(!result.Output.Contains("do-not-print-this"), name + ": secret leaked to output");
    if (option == "--check-config")
        Require(File.ReadAllText(configPath) == before, name + ": check-only modified configuration");
    Console.WriteLine("PASS " + name);
    passed++;
}

/**
 * Executes the prebuilt launcher with isolated settings and closed standard input.
 * @param option Configuration-only operation to execute.
 * @return Exit status and captured output.
 */
async Task<(int Code, string Output)> Run(string option)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repo, UseShellExecute = false,
        RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
    };
    foreach (var argument in new[] { "run", "--file", "scripts/StartSupabaseDev.cs", "--no-build", "--", option })
        start.ArgumentList.Add(argument);
    foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("Auth0__") || key.StartsWith("Authentication__Bearer__")).ToArray())
        start.Environment.Remove(key);
    start.Environment["TRAVELTRACKER_LAUNCH_CONFIG"] = configPath;
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
