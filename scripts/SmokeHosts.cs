#:property PublishAot=false

using System.Diagnostics;
using System.Text.RegularExpressions;

// starts each service host on its own, with every other host stopped, and checks that /health answers.
// usage: dotnet build TravelTracker.slnx -c Release && dotnet run --file scripts/SmokeHosts.cs
// by default only validates host settings. SMOKE_POSTGRES opts into migrations on six disposable service databases.
var configuration = Environment.GetEnvironmentVariable("CONFIGURATION") ?? "Release";
var postgres = Environment.GetEnvironmentVariable("SMOKE_POSTGRES");
var logs = Environment.GetEnvironmentVariable("SMOKE_LOGS") is { Length: > 0 } configured ? configured : Directory.CreateTempSubdirectory("traveltracker-smoke.").FullName;
Directory.CreateDirectory(logs);

var hosts = new (string Name, int Port)[] { ("Attractions", 5104), ("Transport", 5105), ("Accommodations", 5106), ("Settings", 5107), ("Trips", 5108), ("Users", 5101) };
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
var failed = false;

foreach (var (name, port) in hosts)
{
    var dll = Path.Combine("src", $"TravelTracker.{name}.Api", "bin", configuration, "net10.0", $"TravelTracker.{name}.Api.dll");
    var log = Path.Combine(logs, $"{name}.log");
    if (!File.Exists(dll))
    {
        Console.WriteLine($"FAIL {name}: {dll} is missing, build the solution first");
        failed = true;
        continue;
    }

    Console.WriteLine($"INFO starting {name} alone on port {port} (log: {log})");
    var startInfo = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    startInfo.ArgumentList.Add(dll);
    var settings = new Dictionary<string, string>
    {
        ["ASPNETCORE_ENVIRONMENT"] = "Testing",
        ["DOTNET_ENVIRONMENT"] = "Testing",
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
        [$"ConnectionStrings__{name}"] = postgres is { Length: > 0 } ? $"{postgres};Database=traveltracker_{name.ToLowerInvariant()}" : "Host=127.0.0.1;Port=1;Database=unused",
        ["Database__ApplyMigrations"] = (postgres is { Length: > 0 }).ToString().ToLowerInvariant(),
        ["Authentication__Bearer__Authority"] = "https://identity.example.invalid",
        ["Authentication__Bearer__Issuer"] = "https://identity.example.invalid",
        ["Authentication__Bearer__Audience"] = "traveltracker-api",
        ["TripReferences__AccommodationsBaseUrl"] = "http://127.0.0.1:1",
        ["TripReferences__AttractionsBaseUrl"] = "http://127.0.0.1:1",
        ["TripReferences__TransportBaseUrl"] = "http://127.0.0.1:1",
        ["TripReferences__UsersBaseUrl"] = "https://127.0.0.1:1",
        ["TripReferences__UsersServiceAccessToken"] = "smoke-test-token"
    };
    foreach (var (key, value) in settings) startInfo.Environment[key] = value;

    using var process = Process.Start(startInfo)!;
    var output = new List<string>();
    process.OutputDataReceived += (_, data) => { if (data.Data is not null) lock (output) output.Add(data.Data); };
    process.ErrorDataReceived += (_, data) => { if (data.Data is not null) lock (output) output.Add(data.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();

    var status = "000";
    var body = "";
    for (var attempt = 0; attempt < 40 && !process.HasExited; attempt++)
    {
        try
        {
            using var response = await http.GetAsync($"http://127.0.0.1:{port}/health");
            status = ((int)response.StatusCode).ToString();
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            status = "000";
        }
        if (status == "200") break;
        await Task.Delay(500);
    }

    if (status == "200" && Regex.IsMatch(body, $"\"service\":\"[A-Za-z.]*{name}\""))
        Console.WriteLine($"PASS {name}: /health answered 200 with every other host stopped");
    else
    {
        Console.WriteLine($"FAIL {name}: /health answered {status}; last log lines:");
        string[] tail;
        lock (output) tail = output.TakeLast(15).ToArray();
        foreach (var line in tail) Console.WriteLine(line);
        failed = true;
    }

    if (!process.HasExited) process.Kill(entireProcessTree: true);
    await process.WaitForExitAsync();
    lock (output) File.WriteAllLines(log, output);
}

return failed ? 1 : 0;
