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

const string usage = "dotnet run --file scripts/StartSupabaseDev.cs [-- --setup | --check-config | --apply-migrations]";
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
if (args.Length > 1 || args.Length == 1 && !setupOnly && !checkOnly && !applyMigrations)
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
    var rootCertificate = Path.GetFullPath(Setting(config, "RootCertificate", null,
        "Supabase root certificate file path on THIS machine (download from Database Settings > SSL)", interactive).Trim('"', '\''));
    if (!File.Exists(rootCertificate))
        throw new InvalidOperationException("RootCertificate file does not exist on this machine. Run with --setup to correct its path.");
    config["RootCertificate"] = rootCertificate;
    var supabase = ParseSupabaseUrl(Setting(config, "SupabaseUrl", "Supabase__Url",
        "Supabase > Project Settings > API: Project URL (https://project.supabase.co)", interactive));
    config["SupabaseUrl"] = supabase.AbsoluteUri.TrimEnd('/');
    var publicKey = Setting(config, "PublishableKey", "Supabase__PublishableKey",
        "Supabase publishable key (sb_publishable_...) or legacy anon key; NEVER a secret/service_role key", interactive);
    ValidatePublicKey(publicKey);
    var serviceEmail = Setting(config, "ServiceEmail", "TRAVELTRACKER_SERVICE_EMAIL",
        "Dedicated Supabase Auth service user's email (signed scope must be users.access only)", interactive);
    if (!System.Net.Mail.MailAddress.TryCreate(serviceEmail, out var serviceAddress) || serviceAddress.Address != serviceEmail)
        throw new InvalidOperationException("ServiceEmail must be a valid email address for the dedicated Supabase Auth service user.");
    var authority = new Uri(supabase, "auth/v1");
    const string audience = "authenticated";
    if (!checkOnly)
    {
        await SaveConfigurationAsync(configPath, config, shutdown.Token);
        Console.WriteLine("Saved local launch settings. Passwords, client secrets, and tokens are never saved.");
    }
    if (setupOnly || checkOnly)
    {
        Console.WriteLine("Launch configuration is valid. Database access and Supabase Auth credentials have not been tested.");
        return 0;
    }

    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
    if (listeners.Any(listener => services.Any(service => service.Port == listener.Port)))
        throw new InvalidOperationException("A TravelTracker port is already in use. Stop the existing stack first.");

    await RunDotnetAsync(repo, ["dev-certs", "https", "--check", "--trust"],
        "Trust the Users development certificate with dotnet dev-certs https --trust.", shutdown.Token);
    await RunDotnetAsync(repo, ["build", solution, "--configuration", "Debug"], "TravelTracker build failed.", shutdown.Token);

    var (token, subject, expiry) = await GetServiceTokenAsync(supabase, publicKey, serviceEmail, shutdown.Token);
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
        settings["Authentication__Bearer__ScopeClaimType"] = "scope";
        settings["Authentication__Bearer__Provider"] = "Supabase";
        settings["Authentication__Bearer__ValidAlgorithms__0"] = "ES256";
        settings["Authentication__Bearer__ValidAlgorithms__1"] = "RS256";
        settings["Supabase__Url"] = supabase.AbsoluteUri.TrimEnd('/');
        settings["Supabase__PublishableKey"] = publicKey;
        if (frontend)
        {
            settings["Authentication__Provider"] = "Supabase";
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
    Console.WriteLine($"Press Ctrl+C to stop. Restart before the Supabase service token expires at {expiry.ToLocalTime():g}.");
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
        string[] legacy = ["BackendClientId", "Auth0Domain", "FrontendClientId", "Audience", "FrontendType"];
        if (config.Keys.Any(key => legacy.Contains(key)))
            throw new InvalidOperationException("Saved launch settings use Auth0. Rename .traveltracker-launch.json, then run --setup for Supabase Auth; retain your database host, username and certificate path.");
        string[] allowed = ["PoolerHost", "DatabaseUser", "RootCertificate", "SupabaseUrl", "PublishableKey", "ServiceEmail"];
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
 * Restricts authentication calls to the configured hosted project's HTTPS origin.
 * @param input The project URL copied from Supabase.
 * @return The normalized project origin with a trailing slash.
 */
static Uri ParseSupabaseUrl(string input)
{
    if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
        url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
        url.AbsolutePath != "/" || !url.IsDefaultPort ||
        !Regex.IsMatch(url.Host, @"\A[a-z0-9-]+\.supabase\.co\z"))
        throw new InvalidOperationException("SupabaseUrl must be the HTTPS Project URL (https://project.supabase.co), without a path, query, credentials, or custom port.");
    return url;
}

/**
 * Rejects privileged server keys before any configuration can be saved or sent to a child host.
 * @param value The publishable key or legacy anon JWT.
 */
static void ValidatePublicKey(string value)
{
    if (Regex.IsMatch(value, @"\Asb_publishable_[A-Za-z0-9_-]+\z")) return;
    try
    {
        var parts = value.Split('.');
        if (parts.Length == 3)
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            if (payload.RootElement.TryGetProperty("role", out var role) && role.GetString() == "anon") return;
        }
    }
    catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException) { }
    throw new InvalidOperationException("PublishableKey must be an sb_publishable_ key or legacy anon key. Secret and service_role keys are never accepted.");
}

/**
 * Decodes the transport representation of a JWT segment; this does not authenticate claims.
 * @param segment A base64url segment.
 * @return The decoded bytes.
 */
static byte[] DecodeBase64Url(string segment)
{
    var encoded = segment.Replace('-', '+').Replace('_', '/');
    return Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
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

/**
 * Signs in the dedicated service identity and validates its JWT plus its current Supabase user.
 * @param project The trusted HTTPS Supabase project origin.
 * @param publicKey The public project API key, never a privileged service key.
 * @param email The operator-provisioned service account email.
 * @param cancellationToken Cancels authentication and key retrieval.
 * @return The validated service access token, identity, and expiry.
 */
static async Task<(string Token, string Subject, DateTimeOffset Expiry)> GetServiceTokenAsync(
    Uri project, string publicKey, string email, CancellationToken cancellationToken)
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    http.DefaultRequestHeaders.Add("apikey", publicKey);
    using var request = JsonContent.Create(new Dictionary<string, string>
    {
        ["email"] = email,
        ["password"] = ReadSecret("Supabase Auth SERVICE ACCOUNT password (not the database password)", cancellationToken)
    });
    using var response = await http.PostAsync(new Uri(project, "auth/v1/token?grant_type=password"), request, cancellationToken);
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"Supabase Auth returned HTTP {(int)response.StatusCode}. Check the service account email/password, confirmed email, public API key and access-token hook.");

    using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
    var token = result?.RootElement.TryGetProperty("access_token", out var accessToken) == true ? accessToken.GetString() : null;
    if (token is null || !Regex.IsMatch(token, @"\A[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\z"))
        throw new InvalidOperationException("Supabase Auth did not return an access JWT.");

    var parts = token.Split('.');
    using var header = JsonDocument.Parse(DecodeBase64Url(parts[0]));
    var algorithm = header.RootElement.GetProperty("alg").GetString();
    var keyId = header.RootElement.GetProperty("kid").GetString();
    if (algorithm is not ("ES256" or "RS256") || string.IsNullOrWhiteSpace(keyId))
        throw new InvalidOperationException("Supabase Auth must use an asymmetric ES256 or RS256 signing key. Rotate away from the legacy HS256 key before starting TravelTracker.");
    using var keyResponse = await http.GetAsync(new Uri(project, "auth/v1/.well-known/jwks.json"), cancellationToken);
    if (!keyResponse.IsSuccessStatusCode)
        throw new InvalidOperationException("Supabase Auth signing keys could not be retrieved.");
    using var keys = await keyResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
    var matching = keys!.RootElement.GetProperty("keys").EnumerateArray()
        .Where(key => key.TryGetProperty("kid", out var id) && id.GetString() == keyId).ToArray();
    if (matching.Length != 1 || !VerifySignature(matching[0], algorithm, Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), DecodeBase64Url(parts[2])))
        throw new InvalidOperationException("Supabase Auth access token signature is invalid.");

    using var jwt = JsonDocument.Parse(DecodeBase64Url(parts[1]));
    var claims = jwt.RootElement;
    var expiry = DateTimeOffset.FromUnixTimeSeconds(claims.GetProperty("exp").GetInt64());
    var subject = claims.GetProperty("sub").GetString();
    var issuer = new Uri(project, "auth/v1").AbsoluteUri;
    if (!Guid.TryParse(subject, out _) || expiry <= DateTimeOffset.UtcNow ||
        claims.GetProperty("iss").GetString() != issuer ||
        !HasAudience(claims.GetProperty("aud"), "authenticated") ||
        claims.GetProperty("role").GetString() != "authenticated" ||
        claims.TryGetProperty("nbf", out var notBefore) && notBefore.GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
        !claims.TryGetProperty("scope", out var scope) || scope.GetString()?.Trim() != "users.access")
        throw new InvalidOperationException("The service token must be current, issued by this project for authenticated users, and grant users.access only. Check the access-token hook and the dedicated user's admin-managed metadata.");

    using var identityRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(project, "auth/v1/user"));
    identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var identityResponse = await http.SendAsync(identityRequest, cancellationToken);
    if (!identityResponse.IsSuccessStatusCode)
        throw new InvalidOperationException("Supabase Auth could not verify the service account session.");
    using var identity = await identityResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
    if (identity?.RootElement.GetProperty("id").GetString() != subject ||
        !identity.RootElement.TryGetProperty("email_confirmed_at", out var confirmed) || confirmed.ValueKind != JsonValueKind.String)
        throw new InvalidOperationException("The service session must belong to the confirmed Supabase Auth service user.");
    return (token, subject, expiry);
}

/**
 * Matches string and array JWT audience representations.
 * @param value The authenticated token audience claim.
 * @param expected The required audience.
 * @return Whether the required audience is present.
 */
static bool HasAudience(JsonElement value, string expected) => value.ValueKind == JsonValueKind.String
    ? value.GetString() == expected
    : value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == expected);

/**
 * Verifies supported asymmetric JWT signatures using only the project's trusted JWKS.
 * @param key The single matching public signing key.
 * @param algorithm The supported token algorithm.
 * @param data The original JWT signing input.
 * @param signature The token signature bytes.
 * @return Whether the key, algorithm and signature match.
 */
static bool VerifySignature(JsonElement key, string algorithm, byte[] data, byte[] signature)
{
    if (key.TryGetProperty("alg", out var declaredAlgorithm) && declaredAlgorithm.GetString() != algorithm ||
        key.TryGetProperty("use", out var use) && use.GetString() != "sig") return false;
    try
    {
        if (algorithm == "RS256" && key.GetProperty("kty").GetString() == "RSA")
        {
            using var rsa = RSA.Create(new RSAParameters
            {
                Modulus = DecodeBase64Url(key.GetProperty("n").GetString()!),
                Exponent = DecodeBase64Url(key.GetProperty("e").GetString()!)
            });
            return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        if (algorithm == "ES256" && key.GetProperty("kty").GetString() == "EC" && key.GetProperty("crv").GetString() == "P-256")
        {
            using var ec = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = DecodeBase64Url(key.GetProperty("x").GetString()!), Y = DecodeBase64Url(key.GetProperty("y").GetString()!) }
            });
            return ec.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
    }
    catch (Exception error) when (error is CryptographicException or FormatException or InvalidOperationException or KeyNotFoundException) { }
    return false;
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
