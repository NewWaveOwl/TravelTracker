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

const string usage = "dotnet run --file scripts/StartSupabaseDev.cs -- <pooler-host> <database-user> <auth0-m2m-client-id> <root-certificate> [--apply-migrations]";
const string usersUrl = "https://localhost:5101";
const string frontendUrl = "http://localhost:5100";

if (args is ["--help"])
{
    Console.WriteLine(usage);
    return 0;
}

if (args.Length is not (4 or 5) || (args.Length == 5 && args[4] != "--apply-migrations") ||
    !Regex.IsMatch(args[0], @"\A[a-z0-9-]+\.pooler\.supabase\.com\z") ||
    !Regex.IsMatch(args[1], @"\A[a-z0-9_]+\.[a-z0-9]+\z") || string.IsNullOrWhiteSpace(args[2]))
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

    var rootCertificate = Path.GetFullPath(args[3]);
    if (!File.Exists(rootCertificate))
        throw new InvalidOperationException("The Supabase root certificate file does not exist.");

    var authorityValue = Environment.GetEnvironmentVariable("Authentication__Bearer__Authority");
    var issuer = Environment.GetEnvironmentVariable("Authentication__Bearer__Issuer");
    var audience = Environment.GetEnvironmentVariable("Authentication__Bearer__Audience");
    if (!Uri.TryCreate(authorityValue, UriKind.Absolute, out var authority) || authority.Scheme != Uri.UriSchemeHttps ||
        authority.UserInfo.Length != 0 || authority.Query.Length != 0 || authority.Fragment.Length != 0 ||
        string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        throw new InvalidOperationException("Set the Auth0 bearer authority (HTTPS), issuer, and audience before starting the stack.");

    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
    if (listeners.Any(listener => services.Any(service => service.Port == listener.Port)))
        throw new InvalidOperationException("A TravelTracker port is already in use. Stop the existing stack first.");

    await RunDotnetAsync(repo, ["dev-certs", "https", "--check", "--trust"],
        "Trust the Users development certificate with dotnet dev-certs https --trust.", shutdown.Token);
    await RunDotnetAsync(repo, ["build", solution, "--configuration", "Debug"], "TravelTracker build failed.", shutdown.Token);

    var (token, subject, expiry) = await GetServiceTokenAsync(authority, args[2], audience, shutdown.Token);
    var connection = new DbConnectionStringBuilder
    {
        ["Host"] = args[0], ["Port"] = 5432, ["Username"] = args[1],
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
        if (!frontend)
        {
            connection["Database"] = "traveltracker_" + service.ToLowerInvariant();
            settings[$"ConnectionStrings__{service}"] = connection.ConnectionString;
            settings["Database__ApplyMigrations"] = (args.Length == 5).ToString();
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

static string ReadSecret(string prompt, CancellationToken cancellationToken)
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
    return value.Length > 0 ? value.ToString() : throw new InvalidOperationException($"{prompt} is required.");
}
