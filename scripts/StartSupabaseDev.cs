#:property PublishAot=false

using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string usage = "dotnet run --file scripts/StartSupabaseDev.cs [-- --setup | --check-config | --apply-migrations]\nLegacy: dotnet run --file scripts/StartSupabaseDev.cs -- <pooler-host> <database-user> <auth0-m2m-client-id> <root-certificate> [--apply-migrations]";
const string usersUrl = "https://localhost:5101";
const string frontendUrl = "http://localhost:5100";

if (args is ["--help"])
{
    Console.WriteLine(usage);
    return 0;
}

var applyMigrations = args.Contains("--apply-migrations");
var setupOnly = args is ["--setup"];
var checkOnly = args is ["--check-config"];
var positional = args.Where(value => value != "--apply-migrations").ToArray();
if (!setupOnly && !checkOnly && positional.Length is not (0 or 4))
{
    Console.Error.WriteLine(usage);
    return 2;
}

var services = new (string Name, int Port)[]
{
    ("Users", 5101), ("Attractions", 5104), ("Transport", 5105), ("Accommodations", 5106),
    ("Settings", 5107), ("Trips", 5108), ("Frontend", 5100)
};
var repo = Directory.GetCurrentDirectory();
var processes = new List<Process>();
using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler stop = (_, signal) =>
{
    signal.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += stop;
using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, signal =>
{
    signal.Cancel = true;
    shutdown.Cancel();
});

try
{
    var solution = Path.Combine(repo, "TravelTracker.slnx");
    if (!File.Exists(solution))
        throw new InvalidOperationException("Run the launcher from the TravelTracker repository root.");

    var configPath = Path.GetFullPath(Environment.GetEnvironmentVariable("TRAVELTRACKER_LAUNCH_CONFIG")
        ?? Path.Combine(repo, ".traveltracker-launch.json"));
    var config = LoadConfiguration(configPath);
    if (positional.Length == 4 && !setupOnly && !checkOnly)
    {
        config["PoolerHost"] = positional[0];
        config["DatabaseUser"] = positional[1];
        config["BackendClientId"] = positional[2];
        config["RootCertificate"] = positional[3];
    }
    var interactive = !checkOnly && !Console.IsInputRedirected;
    var (poolerHost, connectionUser) = ParseConnection(Setting(config, "PoolerHost", null,
        "Supabase > Connect > Direct > Session pooler: paste Host or connection URI (input hidden)",
        interactive, hidden: true, cancellationToken: shutdown.Token));
    config["PoolerHost"] = poolerHost;
    if (connectionUser is not null)
        config["DatabaseUser"] = connectionUser;
    var databaseUser = Setting(config, "DatabaseUser", null,
        "Supabase Session pooler User (include the .project-reference suffix)", interactive);
    if (!Regex.IsMatch(databaseUser, @"\A[a-z0-9_]+\.[a-z0-9]+\z"))
        throw new InvalidOperationException("DatabaseUser must be the Session pooler username including its .project-reference suffix.");
    var backendClientId = Setting(config, "BackendClientId", null,
        "Auth0 > Applications > machine-to-machine application > Client ID", interactive);
    var rootCertificate = Path.GetFullPath(Setting(config, "RootCertificate", null,
        "Supabase root certificate file path on THIS machine (download from Database Settings > SSL)", interactive).Trim('"', '\''));
    if (!File.Exists(rootCertificate))
        throw new InvalidOperationException("RootCertificate file does not exist on this machine. Run with --setup to correct its path.");
    config["RootCertificate"] = rootCertificate;
    var domain = Setting(config, "Auth0Domain", "Auth0__Domain",
        "Auth0 > frontend application > Settings > Domain (for example dev-example.us.auth0.com)", interactive,
        fallback: Environment.GetEnvironmentVariable("Authentication__Bearer__Authority"));
    domain = domain.TrimEnd('/');
    if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        domain = domain[8..];
    if (!Regex.IsMatch(domain, @"\A[a-z0-9]+(?:[a-z0-9.-]*[a-z0-9])?\z", RegexOptions.IgnoreCase) ||
        !domain.Contains('.') || domain.Contains(".."))
        throw new InvalidOperationException("Auth0Domain must be your Auth0 tenant hostname, without a path, query, or credentials.");
    config["Auth0Domain"] = domain;
    var frontendClientId = Setting(config, "FrontendClientId", "Auth0__ClientId",
        "Auth0 > frontend application > Settings > Client ID (different from M2M)", interactive);
    var audience = Setting(config, "Audience", "Authentication__Bearer__Audience",
        "Auth0 API identifier", interactive, fallback: "traveltracker-api");
    var frontendType = Setting(config, "FrontendType", null,
        "Frontend Auth0 application type: spa or regular", interactive, fallback: "spa").ToLowerInvariant();
    if (frontendType is not ("spa" or "regular"))
        throw new InvalidOperationException("FrontendType must be spa or regular.");
    config["FrontendType"] = frontendType;
    var authority = new Uri($"https://{domain}/");
    if (!checkOnly)
    {
        await SaveConfigurationAsync(configPath, config, shutdown.Token);
        Console.WriteLine("Saved local launch settings. Passwords, client secrets, and tokens are never saved.");
    }
    if (setupOnly || checkOnly)
    {
        Console.WriteLine("Launch configuration is valid. Database access and Auth0 credentials have not been tested.");
        return 0;
    }

    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
    if (listeners.Any(listener => services.Any(service => service.Port == listener.Port)))
        throw new InvalidOperationException("A TravelTracker port is already in use. Stop the existing stack first.");

    await RunDotnetAsync(repo, ["dev-certs", "https", "--check", "--trust"],
        "Trust the Users development certificate with dotnet dev-certs https --trust.", shutdown.Token);
    await RunDotnetAsync(repo, ["build", solution, "--configuration", "Debug"], "TravelTracker build failed.", shutdown.Token);

    var frontendSecret = frontendType == "regular"
        ? ReadSecret("Auth0 FRONTEND client secret (Regular Web Application)", shutdown.Token) : null;
    var (token, subject, expiry) = await GetServiceTokenAsync(authority, backendClientId, audience, shutdown.Token);
    var connection = new DbConnectionStringBuilder
    {
        ["Host"] = poolerHost, ["Port"] = 5432, ["Username"] = databaseUser,
        ["SSL Mode"] = "VerifyFull", ["Root Certificate"] = rootCertificate, ["Maximum Pool Size"] = 5,
        ["Password"] = ReadSecret("Supabase database password", shutdown.Token)
    };

    using var probe = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(2)
    };
    foreach (var (service, port) in services)
    {
        shutdown.Token.ThrowIfCancellationRequested();
        var frontend = service == "Frontend";
        var project = frontend ? "TravelTracker.Frontend" : $"TravelTracker.{service}.Api";
        var folder = Path.Combine(repo, "src", project);
        var origin = service == "Users" ? usersUrl : $"http://localhost:{port}";
        var start = Dotnet(folder, Path.Combine(folder, "bin", "Debug", "net10.0", project + ".dll"));
        var settings = start.Environment;
        settings["ASPNETCORE_ENVIRONMENT"] = "Development";
        settings["DOTNET_ENVIRONMENT"] = "Development";
        settings["ASPNETCORE_URLS"] = origin;
        settings["Authentication__Bearer__Authority"] = authority.AbsoluteUri;
        settings["Authentication__Bearer__Issuer"] = authority.AbsoluteUri;
        settings["Authentication__Bearer__Audience"] = audience;
        if (frontend)
        {
            settings["Auth0__Domain"] = domain;
            settings["Auth0__ClientId"] = frontendClientId;
            settings["Auth0__Audience"] = audience;
            settings["Auth0__ClientSecret"] = frontendSecret ?? "";
        }
        if (!frontend)
        {
            connection["Database"] = "traveltracker_" + service.ToLowerInvariant();
            settings[$"ConnectionStrings__{service}"] = connection.ConnectionString;
            settings["Database__ApplyMigrations"] = applyMigrations.ToString();
            settings["Database__SeedExamples"] = "false";
        }

        switch (service)
        {
            case "Users":
                settings["UsersAccess__AllowedServiceSubjects__0"] = subject;
                break;
            case "Trips":
                settings["TripReferences__UsersBaseUrl"] = usersUrl;
                settings["TripReferences__UsersServiceAccessToken"] = token;
                break;
            case "Attractions" or "Accommodations" or "Settings":
                settings["Users__InternalBaseUrl"] = usersUrl;
                settings["Users__ServiceAccessToken"] = token;
                break;
        }

        Console.WriteLine($"Starting {service} at {origin}");
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {service}.");
        processes.Add(process);
        await WaitForHostAsync(probe, process, service, origin, shutdown.Token);
    }
    connection.Clear();

    Console.WriteLine($"TravelTracker is running at {frontendUrl}. Map View: {frontendUrl}/map");
    Console.WriteLine($"Press Ctrl+C to stop. Restart before the Auth0 service token expires at {expiry.ToLocalTime():g}.");
    var exited = await Task.WhenAny(processes.Select(process => process.WaitForExitAsync(shutdown.Token)));
    await exited;
    if (!shutdown.IsCancellationRequested)
        throw new InvalidOperationException("A TravelTracker host stopped. See its output above.");
    return 0;
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
finally
{
    Console.CancelKeyPress -= stop;
    foreach (var process in processes.AsEnumerable().Reverse())
    {
        StopProcess(process);
        process.Dispose();
    }
}

/**
 * Reads only the supported non-secret local settings.
 * @param path Local JSON configuration file.
 * @return Settings, or an empty configuration on first run.
 */
static Dictionary<string, string> LoadConfiguration(string path)
{
    if (!File.Exists(path))
        return new();
    try
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("Local launch settings must be a JSON object.");
        string[] allowed = ["PoolerHost", "DatabaseUser", "BackendClientId", "RootCertificate",
            "Auth0Domain", "FrontendClientId", "Audience", "FrontendType"];
        if (config.Keys.Any(key => !allowed.Contains(key)))
            throw new InvalidOperationException("Local launch settings contain unsupported fields. Store only non-secret launcher settings.");
        return config;
    }
    catch (JsonException)
    {
        throw new InvalidOperationException("Local launch settings contain invalid JSON. Correct or rename .traveltracker-launch.json, then run --setup.");
    }
}

/**
 * Resolves a setting from the environment, saved values, or an interactive first-run prompt.
 * @param config Non-secret settings updated with the chosen value.
 * @param key Setting name.
 * @param environment Optional environment override.
 * @param prompt Instructions shown when input is required.
 * @param interactive Whether prompting is allowed.
 * @param fallback Default used when no saved value exists.
 * @param hidden Whether typed input may contain a connection-string password.
 * @param cancellationToken Cancels secret-style input.
 * @return The chosen nonempty setting.
 */
string Setting(Dictionary<string, string> config, string key, string? environment, string prompt,
    bool interactive, string? fallback = null, bool hidden = false, CancellationToken cancellationToken = default)
{
    var value = (environment is null ? null : Environment.GetEnvironmentVariable(environment))
        ?? config.GetValueOrDefault(key) ?? fallback;
    if (interactive && (setupOnly || string.IsNullOrWhiteSpace(value)))
    {
        // Never echo a saved URI: old or hand-edited input may contain a password.
        var defaultHint = string.IsNullOrWhiteSpace(value) ? "" : hidden ? " [Enter keeps saved value]" : $" [{value}]";
        var label = prompt + defaultHint;
        if (hidden)
            value = ReadSecret(label, cancellationToken, value);
        else
        {
            Console.Write(label + ": ");
            var input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
                value = input.Trim();
        }
    }
    if (string.IsNullOrWhiteSpace(value) || (key != "PoolerHost" && value.Contains("YOUR-", StringComparison.OrdinalIgnoreCase)) ||
        value.Contains("your-tenant", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"{key} is missing or contains an example placeholder. {prompt}. Run --setup in an interactive terminal.");
    config[key] = value;
    return value;
}

/**
 * Extracts session-pooler host and username while discarding any URI password.
 * @param input A hostname or PostgreSQL URI.
 * @return The validated pooler host and optional URI username.
 */
static (string Host, string? User) ParseConnection(string input)
{
    var host = input.Trim();
    string? user = null;
    if (host.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase) || host.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase))
    {
        // Supabase's [YOUR-PASSWORD] placeholder is not URI syntax; discard the password before parsing.
        var schemeEnd = host.IndexOf("://", StringComparison.Ordinal) + 3;
        var at = host.LastIndexOf('@');
        if (at <= schemeEnd)
            throw new InvalidOperationException("The PostgreSQL URI must contain a username and host. Copy the Session pooler URI from Supabase.");
        var credentials = host[schemeEnd..at];
        user = Uri.UnescapeDataString(credentials.Split(':', 2)[0]);
        if (!Uri.TryCreate("postgresql://" + host[(at + 1)..], UriKind.Absolute, out var uri) || uri.Port is not (-1 or 5432))
            throw new InvalidOperationException("Use the Supabase Session pooler on port 5432, rather than the transaction pooler.");
        host = uri.Host;
    }
    if (!Regex.IsMatch(host, @"\A[a-z0-9-]+\.pooler\.supabase\.com\z"))
        throw new InvalidOperationException("PoolerHost must end in .pooler.supabase.com. In Supabase choose Connect > Direct > Session pooler; the db.* direct host will not work here.");
    return (host, user);
}

/**
 * Atomically saves the normalized non-secret settings after validation.
 * @param path Local configuration destination.
 * @param config Validated configuration containing no credentials.
 * @param cancellationToken Cancels the write.
 * @return A task completing after replacement succeeds.
 */
static async Task SaveConfigurationAsync(string path, Dictionary<string, string> config, CancellationToken cancellationToken)
{
    var temporary = path + ".tmp";
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    File.Move(temporary, path, overwrite: true);
}

static ProcessStartInfo Dotnet(string folder, params string[] arguments)
{
    var start = new ProcessStartInfo("dotnet") { WorkingDirectory = folder, UseShellExecute = false };
    foreach (var argument in arguments)
        start.ArgumentList.Add(argument);
    return start;
}

static async Task RunDotnetAsync(string repo, string[] arguments, string failure, CancellationToken cancellationToken)
{
    using var process = Process.Start(Dotnet(repo, arguments)) ?? throw new InvalidOperationException(failure);
    try
    {
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(failure);
    }
    finally { StopProcess(process); }
}

static void StopProcess(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
    catch (InvalidOperationException) when (process.HasExited) { }
    catch (System.ComponentModel.Win32Exception error) { Console.Error.WriteLine($"Could not stop host: {error.Message}"); }
}

/**
 * Waits for this host's HTTP health response before starting the next dependent host.
 * @param probe Client for local health requests.
 * @param process The child process that must remain alive.
 * @param service The expected host identity.
 * @param origin The host's loopback URL.
 * @param cancellationToken Cancels startup.
 * @return A task that completes when the host responds successfully.
 */
static async Task WaitForHostAsync(HttpClient probe, Process process, string service, string origin, CancellationToken cancellationToken)
{
    var frontend = service == "Frontend";
    var elapsed = Stopwatch.StartNew();
    while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (process.HasExited)
            throw new InvalidOperationException($"{service} exited during startup. See its output above.");

        try
        {
            using var response = await probe.GetAsync(origin + (frontend ? "/" : "/health"), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK && !process.HasExited)
            {
                if (frontend)
                    return;
                using var health = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                if (health?.RootElement.TryGetProperty("service", out var name) == true &&
                    name.GetString()?.Split('.').Last() == service)
                    return;
            }
        }
        catch (HttpRequestException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        await Task.Delay(250, cancellationToken);
    }
    throw new InvalidOperationException($"{service} did not become ready at {origin}. See its output above.");
}

static async Task<(string Token, string Subject, DateTimeOffset Expiry)> GetServiceTokenAsync(
    Uri authority, string clientId, string audience, CancellationToken cancellationToken)
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    using var request = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_secret"] = ReadSecret("Auth0 machine-to-machine client secret", cancellationToken),
        ["audience"] = audience,
        ["scope"] = "users.access"
    });
    using var response = await http.PostAsync(authority.AbsoluteUri.TrimEnd('/') + "/oauth/token", request, cancellationToken);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"Auth0 returned HTTP {(int)response.StatusCode}. Check the client credentials, audience, and users.access grant.");

    using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
    var token = result?.RootElement.GetProperty("access_token").GetString();
    if (token is null || !Regex.IsMatch(token, @"\A[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z"))
        throw new InvalidOperationException("Auth0 did not return a JWT. Check the configured API audience.");

    // The APIs verify the signature. The launcher reads Auth0's response to configure the caller and expiry warning.
    var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
    using var jwt = JsonDocument.Parse(Convert.FromBase64String(payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
    var claims = jwt.RootElement;
    var expiry = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
    var subject = claims.GetProperty("sub").GetString();
    if (string.IsNullOrWhiteSpace(subject) || expiry <= DateTimeOffset.UtcNow ||
        !claims.TryGetProperty("scope", out var scope) || scope.GetString()?.Split(' ').Contains("users.access") != true)
        throw new InvalidOperationException("The backend token must be current and include users.access.");
    return (token, subject, expiry);
}

/**
 * Reads terminal input without echoing credentials or connection-string passwords.
 * @param prompt Instructions containing no secret values.
 * @param cancellationToken Cancels input on shutdown.
 * @param fallback Optional saved value retained on Enter during setup.
 * @return The entered value or the saved fallback.
 */
static string ReadSecret(string prompt, CancellationToken cancellationToken, string? fallback = null)
{
    if (Console.IsInputRedirected)
        throw new InvalidOperationException("Run the launcher in an interactive terminal so it can prompt for secrets.");

    Console.Write($"{prompt}: ");
    var value = new StringBuilder();
    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            value.Length--;
        else if (!char.IsControl(key.KeyChar))
            value.Append(key.KeyChar);
    }
    Console.WriteLine();
    return value.Length > 0 ? value.ToString() : fallback ?? throw new InvalidOperationException($"{prompt} is required.");
}
