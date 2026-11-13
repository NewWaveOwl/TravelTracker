#:property PublishAot=false

using System.Diagnostics;
using System.Text;
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
    ["RootCertificate"] = certificate,
    ["SupabaseUrl"] = "https://exampleproject.supabase.co",
    ["PublishableKey"] = "sb_publishable_offline_fixture",
    ["ServiceEmail"] = "service@example.test"
};
var passed = 0;
try
{
    await Check("valid saved settings and certificate path with spaces", new(baseline), "--check-config", 0, "configuration is valid");
    await Check("first run without a terminal explains missing settings", new(), "--check-config", 1, "PoolerHost is missing");
    await Check("direct host explains session pooler requirement", Changed("PoolerHost", "db.example.supabase.co"), "--check-config", 1, "Session pooler");
    await Check("pooler username requires project suffix", Changed("DatabaseUser", "postgres"), "--check-config", 1, "project-reference suffix");
    await Check("missing certificate explains local path", Changed("RootCertificate", Path.Combine(temporary, "missing.crt")), "--check-config", 1, "does not exist on this machine");
    await Check("placeholder public key rejected", Changed("PublishableKey", "YOUR-PUBLISHABLE-KEY"), "--check-config", 1, "example placeholder");
    await Check("project URL normalized", Changed("SupabaseUrl", "https://exampleproject.supabase.co/"), "--setup", 0, "configuration is valid");
    Require(JsonDocument.Parse(File.ReadAllText(configPath)).RootElement.GetProperty("SupabaseUrl").GetString() == "https://exampleproject.supabase.co", "Project URL normalization failed");
    await Check("project URL with path rejected", Changed("SupabaseUrl", "https://exampleproject.supabase.co/auth/v1"), "--check-config", 1, "without a path");
    await Check("non-Supabase project URL rejected", Changed("SupabaseUrl", "https://attacker.example.test"), "--check-config", 1, "HTTPS Project URL");
    await Check("insecure project URL rejected", Changed("SupabaseUrl", "http://exampleproject.supabase.co"), "--check-config", 1, "HTTPS Project URL");
    await Check("project URL credentials rejected", Changed("SupabaseUrl", "https://do-not-print-this@exampleproject.supabase.co"), "--check-config", 1, "HTTPS Project URL");
    await Check("secret API key rejected", Changed("PublishableKey", "sb_secret_do-not-print-this"), "--check-config", 1, "never accepted");
    await Check("legacy anon key accepted", Changed("PublishableKey", JwtKey("anon")), "--check-config", 0, "configuration is valid");
    await Check("legacy service_role key rejected", Changed("PublishableKey", JwtKey("service_role")), "--check-config", 1, "never accepted");
    await Check("invalid service email rejected", Changed("ServiceEmail", "not-an-email"), "--check-config", 1, "valid email address");
    var legacy = new Dictionary<string, string>(baseline) { ["Auth0Domain"] = "old.example.auth0.com" };
    await Check("legacy Auth0 settings explain migration", legacy, "--setup", 1, "use Auth0");
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
 * Executes the launcher with isolated settings and closed standard input.
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
    foreach (var argument in new[] { "run", "--file", "scripts/StartSupabaseDev.cs", "--", option })
        start.ArgumentList.Add(argument);
    foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("Auth0__") || key.StartsWith("Supabase__") ||
        key.StartsWith("Authentication__Bearer__") || key == "TRAVELTRACKER_SERVICE_EMAIL").ToArray())
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

/**
 * Creates an offline API-key-shaped fixture; no signature or live project is implied.
 * @param role The API-key role under validation.
 * @return A syntactically valid JWT-shaped fixture.
 */
static string JwtKey(string role)
{
    var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { role })))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return "eyJhbGciOiJIUzI1NiJ9." + payload + ".offline";
}
