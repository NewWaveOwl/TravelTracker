using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Frontend.Api.Settings;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * plays the Settings service in process: serves the saved revision, enforces If-Match like the real host and
 * lets tests change the stored values elsewhere or force a failure status.
 */
public sealed class FakeSettingsService : HttpMessageHandler
{
    static readonly string[] Themes = ["system", "light", "dark"];
    static readonly string[] Modes = ["car", "bus", "train", "pedestrian", "bike", "plane"];

    /** the revision the next read reports; every accepted save advances it by one. */
    public long Revision { get; private set; }

    /** the values the service currently holds. */
    public SettingsDto Stored { get; private set; } = new("system", 0, 0, 2, "car", "UTC");

    /** every request as method and raw If-Match header, in arrival order. */
    public List<(HttpMethod Method, string? IfMatch)> Requests { get; } = [];

    /** replaces the response for matching requests; returning null lets the fake answer normally. */
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    /** when set, reads wait for this gate before answering, so tests can observe the loading state. */
    public TaskCompletionSource<bool>? ReadGate { get; set; }

    /** when set, saves wait for this gate before answering, so tests can observe the saving state. */
    public TaskCompletionSource<bool>? WriteGate { get; set; }

    /** the number of saves the service accepted. */
    public int AcceptedWrites { get; private set; }

    /**
     * simulates another browser saving first, so the next If-Match from this page is stale.
     * @param settings The values the other browser saved.
     */
    public void ChangeElsewhere(SettingsDto settings)
    {
        Stored = settings;
        Revision++;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryGetValues("If-Match", out var ifMatchValues);
        Requests.Add((request.Method, ifMatchValues?.SingleOrDefault()));
        if (Override?.Invoke(request) is { } forced) return forced;
        if (request.Method == HttpMethod.Get)
        {
            if (ReadGate is { } readGate) await readGate.Task;
            return Answer(HttpStatusCode.OK, Stored);
        }
        if (request.Method != HttpMethod.Put) return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        if (WriteGate is { } writeGate) await writeGate.Task;
        if (ifMatchValues is null) return new HttpResponseMessage(HttpStatusCode.PreconditionRequired);
        var candidate = await request.Content!.ReadFromJsonAsync<SettingsDto>(cancellationToken);
        var errors = Validate(candidate!);
        if (errors.Count > 0) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { errors }) };
        if (ifMatchValues.Single() != Tag()) return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        Stored = candidate!;
        Revision++;
        AcceptedWrites++;
        return Answer(HttpStatusCode.OK, Stored);
    }

    string Tag() => $"\"{Revision}\"";

    HttpResponseMessage Answer(HttpStatusCode status, SettingsDto body)
    {
        var response = new HttpResponseMessage(status) { Content = JsonContent.Create(body) };
        response.Headers.TryAddWithoutValidation("ETag", Tag());
        return response;
    }

    static Dictionary<string, string[]> Validate(SettingsDto candidate)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Themes.Contains(candidate.Theme)) errors["theme"] = ["Theme must be system, light or dark."];
        if (candidate.DefaultMapLatitude is < -90m or > 90m) errors["defaultMapLatitude"] = ["Latitude must be between -90 and 90."];
        if (candidate.DefaultMapLongitude is < -180m or > 180m) errors["defaultMapLongitude"] = ["Longitude must be between -180 and 180."];
        if (candidate.DefaultMapZoom is < 0 or > 20) errors["defaultMapZoom"] = ["Zoom must be between 0 and 20."];
        if (!Modes.Contains(candidate.PreferredTransportMode)) errors["preferredTransportMode"] = ["Transport must be car, bus, train, pedestrian, bike or plane."];
        return errors;
    }
}
