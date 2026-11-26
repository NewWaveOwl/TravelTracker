#:property PublishAot=false

using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string usage = "dotnet run --file scripts/StartDev.cs [-- [--local-db | --supabase] [--setup | --check-config] [--apply-migrations]]";
const string usersUrl = "https://localhost:5101";
const string frontendUrl = "http://localhost:5100";
const string audience = "traveltracker-api";

if (args is ["--help"])
{
    Console.WriteLine(usage);
    return 0;
}

string[] knownFlags = ["--local-db", "--supabase", "--setup", "--check-config", "--apply-migrations"];
var localDb = args.Contains("--local-db");
var supabase = args.Contains("--supabase");
var applyMigrations = args.Contains("--apply-migrations");
var setupOnly = args.Contains("--setup");
var checkOnly = args.Contains("--check-config");
if (args.Any(argument => !knownFlags.Contains(argument)) || args.Distinct().Count() != args.Length || setupOnly && checkOnly || localDb && (supabase || setupOnly || checkOnly))
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
Process? localDatabase = null;
DirectoryInfo? trustFolder = null;
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
    var interactive = !checkOnly && !Console.IsInputRedirected;
    // any postgres server works: set TRAVELTRACKER_DATABASE to an Npgsql connection string (see scripts/StartLocalDb.cs),
    // or pass --supabase to build one from a Supabase session pooler. Supabase is only a preset for the connection, nothing else.
    var rawDatabase = Environment.GetEnvironmentVariable("TRAVELTRACKER_DATABASE");
    string? poolerHost = null, databaseUser = null, rootCertificate = null;
    if ((supabase || localDb) && rawDatabase is not null)
        throw new InvalidOperationException("TRAVELTRACKER_DATABASE and --supabase or --local-db both choose the database. Unset one of them.");
    if (supabase)
    {
        (poolerHost, var connectionUser) = ParseConnection(Setting(config, "PoolerHost", null,
            "Supabase > Connect > Direct > Session pooler: paste Host or connection URI (input hidden)",
            interactive, hidden: true, cancellationToken: shutdown.Token));
        config["PoolerHost"] = poolerHost;
        if (connectionUser is not null)
            config["DatabaseUser"] = connectionUser;
        databaseUser = Setting(config, "DatabaseUser", null,
            "Supabase Session pooler User (include the .project-reference suffix)", interactive);
        if (!Regex.IsMatch(databaseUser, @"\A[a-z0-9_]+\.[a-z0-9]+\z"))
            throw new InvalidOperationException("DatabaseUser must be the Session pooler username including its .project-reference suffix.");
        rootCertificate = Path.GetFullPath(Setting(config, "RootCertificate", null,
            "Supabase root certificate file path on THIS machine (download from Database Settings > SSL)", interactive).Trim('"', '\''));
        if (!File.Exists(rootCertificate))
            throw new InvalidOperationException("RootCertificate file does not exist on this machine. Run with --supabase --setup to correct its path.");
        config["RootCertificate"] = rootCertificate;
        Console.WriteLine("Using the Supabase session pooler as the PostgreSQL server.");
        if (!checkOnly)
        {
            await SaveConfigurationAsync(configPath, config, shutdown.Token);
            Console.WriteLine("Saved local launch settings. Passwords and tokens are never saved.");
        }
    }
    else
    {
        if (rawDatabase is null && (setupOnly || checkOnly))
            throw new InvalidOperationException("No database is chosen. Set TRAVELTRACKER_DATABASE to an Npgsql connection string, or pass --supabase.");
        if (rawDatabase is not null)
            Console.WriteLine("Using the PostgreSQL server from TRAVELTRACKER_DATABASE.");
        else if (!setupOnly && !checkOnly && !localDb)
            rawDatabase = ReadSecret("PostgreSQL connection string (Npgsql format, input hidden; or set TRAVELTRACKER_DATABASE)", shutdown.Token);
        try
        {
            _ = new DbConnectionStringBuilder { ConnectionString = rawDatabase ?? "" };
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("The PostgreSQL connection string is not valid Npgsql syntax, for example: Host=localhost;Username=postgres;Password=...");
        }
    }
    if (setupOnly || checkOnly)
    {
        Console.WriteLine("Launch configuration is valid. Database access has not been tested.");
        return 0;
    }

    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
    if (listeners.Any(listener => services.Any(service => service.Port == listener.Port)))
        throw new InvalidOperationException("A TravelTracker port is already in use. Stop the existing stack first.");

    if (OperatingSystem.IsLinux())
    {
        // dev-certs cannot mark the certificate trusted on most distributions, and only the hosts calling Users over https need
        // it, so export the public certificate and point OpenSSL (and so every child process) at it for this launch only
        await RunDotnetAsync(repo, ["dev-certs", "https"], "Could not create the Users development certificate.", shutdown.Token);
        trustFolder = Directory.CreateTempSubdirectory("traveltracker-trust-");
        await RunDotnetAsync(repo, ["dev-certs", "https", "--export-path", Path.Combine(trustFolder.FullName, "users-dev.pem"), "--format", "Pem"],
            "Could not export the Users development certificate.", shutdown.Token);
        Environment.SetEnvironmentVariable("SSL_CERT_DIR", trustFolder.FullName + Path.PathSeparator
            + (Environment.GetEnvironmentVariable("SSL_CERT_DIR") is { Length: > 0 } existing ? existing : "/etc/ssl/certs"));
    }
    else
        await RunDotnetAsync(repo, ["dev-certs", "https", "--check", "--trust"],
            "Trust the Users development certificate with dotnet dev-certs https --trust.", shutdown.Token);
    await RunDotnetAsync(repo, ["build", solution, "--configuration", "Debug"], "TravelTracker build failed.", shutdown.Token);

    // the container script is a child that removes its container once its stdin closes, so the database ends with this launch
    if (localDb)
        (localDatabase, rawDatabase) = await StartLocalDatabaseAsync(repo, shutdown.Token);

    // nothing here is saved: the signing key and service credential live only as long as this launch
    using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var privateKey = signingKey.ExportPkcs8PrivateKeyPem();
    var publicKey = signingKey.ExportSubjectPublicKeyInfoPem();
    var serviceSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    string? token = null;
    DateTimeOffset expiry = default;
    var connection = rawDatabase is not null
        ? new DbConnectionStringBuilder { ConnectionString = rawDatabase }
        : new DbConnectionStringBuilder
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
        settings["Authentication__Bearer__PublicKey"] = publicKey;
        settings["Authentication__Bearer__Issuer"] = usersUrl;
        settings["Authentication__Bearer__Audience"] = audience;
        settings["Authentication__Bearer__ScopeClaimType"] = "scope";
        settings["Authentication__Bearer__ValidAlgorithms__0"] = "ES256";
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
                settings["Authentication__Issuer__PrivateKey"] = privateKey;
                settings["Authentication__Issuer__ServiceLifetimeMinutes"] = "720";
                settings["UsersAccess__ServiceToken"] = serviceSecret;
                break;
            case "Trips":
                settings["TripReferences__UsersBaseUrl"] = usersUrl;
                settings["TripReferences__UsersServiceAccessToken"] = token!;
                break;
            case "Attractions" or "Accommodations" or "Settings":
                settings["Users__InternalBaseUrl"] = usersUrl;
                settings["Users__ServiceAccessToken"] = token!;
                break;
        }

        Console.WriteLine($"Starting {service} at {origin}");
        var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {service}.");
        processes.Add(process);
        await WaitForHostAsync(probe, process, service, origin, shutdown.Token);
        // the other backends need a users.access token, which only a running Users host can issue
        if (service == "Users")
            (token, expiry) = await GetServiceTokenAsync(usersUrl, serviceSecret, shutdown.Token);
    }
    connection.Clear();

    Console.WriteLine($"TravelTracker is running at {frontendUrl}. Map View: {frontendUrl}/map");
    Console.WriteLine($"Press Ctrl+C to stop. Restart before the service token expires at {expiry.ToLocalTime():g}.");
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
    trustFolder?.Delete(recursive: true);
    if (localDatabase is not null)
    {
        localDatabase.StandardInput.Close();
        if (!localDatabase.WaitForExit(20000))
            Console.Error.WriteLine("The local database did not stop in 20 seconds; remove traveltracker-dev-pg by hand.");
        StopProcess(localDatabase);
        localDatabase.Dispose();
    }
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
        string[] allowed = ["PoolerHost", "DatabaseUser", "RootCertificate"];
        // earlier launcher versions saved Supabase Auth settings; sign-in no longer uses them, so say so instead of silently dropping them
        string[] obsolete = ["SupabaseUrl", "PublishableKey", "ServiceEmail", "BackendClientId", "Auth0Domain", "FrontendClientId", "Audience", "FrontendType"];
        foreach (var key in config.Keys.Where(key => obsolete.Contains(key)).ToArray())
        {
            Console.WriteLine($"Ignoring obsolete launch setting {key}: sign-in now runs inside TravelTracker.");
            config.Remove(key);
        }
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

/**
 * Runs scripts/StartLocalDb.cs as a child and waits until its database is ready.
 * @param repo Repository root.
 * @param cancellationToken Cancels startup.
 * @return The child, which stops the database when its stdin closes, and the connection string it printed.
 */
static async Task<(Process Child, string Connection)> StartLocalDatabaseAsync(string repo, CancellationToken cancellationToken)
{
    var start = Dotnet(repo, "run", "--file", Path.Combine("scripts", "StartLocalDb.cs"), "--", "--follow-parent");
    start.RedirectStandardInput = true;
    start.RedirectStandardOutput = true;
    var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the local database script.");
    const string marker = "TRAVELTRACKER_DATABASE=";
    try
    {
        while (await child.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            Console.WriteLine("[db] " + line);
            if (line.StartsWith(marker))
            {
                // keep draining so the child never blocks on a full pipe
                _ = child.StandardOutput.ReadToEndAsync(CancellationToken.None);
                return (child, line[marker.Length..].Trim('"'));
            }
        }
        throw new InvalidOperationException("The local database script stopped before the database was ready. See its output above.");
    }
    catch
    {
        child.StandardInput.Close();
        child.WaitForExit(20000);
        StopProcess(child);
        throw;
    }
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

/**
 * Asks the running Users host for its backend-only access token.
 * @param users The Users host origin.
 * @param serviceSecret The launch-only credential the Users host was started with.
 * @param cancellationToken Cancels the request.
 * @return The token and when it expires.
 */
static async Task<(string Token, DateTimeOffset Expiry)> GetServiceTokenAsync(string users, string serviceSecret, CancellationToken cancellationToken)
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    using var request = new HttpRequestMessage(HttpMethod.Post, users + "/api/v1/auth/service-token");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serviceSecret);
    using var response = await http.SendAsync(request, cancellationToken);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"The Users host refused to issue the service token (HTTP {(int)response.StatusCode}). See its output above.");
    using var body = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
    var token = body?.RootElement.TryGetProperty("accessToken", out var value) == true ? value.GetString() : null;
    var seconds = body?.RootElement.TryGetProperty("expiresIn", out var lifetime) == true ? lifetime.GetInt32() : 0;
    if (string.IsNullOrEmpty(token) || seconds <= 0)
        throw new InvalidOperationException("The Users host did not return a usable service token.");
    return (token, DateTimeOffset.UtcNow.AddSeconds(seconds));
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
