using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * plays the Accommodations, Attractions, Transport, Settings and Trips services in process, routed by port.
 * the Trips side stores what it accepts so a saved trip can be reopened, and tests can force failures or hold a save open.
 */
public sealed class FakeTripsBackend : HttpMessageHandler
{
    public static readonly Guid Hotel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Museum = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid Park = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid Bus = Guid.Parse("44444444-4444-4444-4444-444444444444");

    readonly Dictionary<Guid, TripDto> stored = [];

    /** every request as method and path with query, in arrival order. */
    public List<(HttpMethod Method, string Path)> Requests { get; } = [];

    /** the raw bodies of the trip creates the service received. */
    public List<string> CreateBodies { get; } = [];

    /** replaces the answer for matching requests; returning null lets the fake answer normally. */
    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    /** when set, trip creates wait for this gate before answering, so tests can observe the saving state. */
    public TaskCompletionSource<bool>? CreateGate { get; set; }

    /** the number of trip creates that arrived, whether or not they were accepted. */
    public int CreateCount => CreateBodies.Count;

    /** the trips the service accepted. */
    public IReadOnlyCollection<TripDto> Stored => stored.Values;

    /**
     * stores a trip as if it had been saved earlier, optionally in another account.
     * @param trip The trip to hold.
     */
    public void Seed(TripDto trip) => stored[trip.TripId] = trip;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        Requests.Add((request.Method, uri.PathAndQuery));
        if (Override?.Invoke(request) is { } forced) return forced;
        var path = uri.AbsolutePath;
        var name = System.Web.HttpUtility.ParseQueryString(uri.Query)["name"] ?? "";
        return (uri.Port, path) switch
        {
            (5106, "/api/v1/accommodations") when request.Method == HttpMethod.Post => await CreateAccommodationAsync(request, cancellationToken),
            (5106, "/api/v1/accommodations") => Json(Match(name, new { accommodationId = Hotel, name = "Hotel Lumiere", address = "1 Rue de Rivoli", lat = 48.86m, lon = 2.34m, visibility = "shared" })),
            (5104, "/api/v1/attractions") => Json(Match(name, new { attractionId = Museum, name = "Louvre", description = (string?)null, rating = (decimal?)null, address = "Rue de Rivoli", lat = 48.86m, lon = 2.33m, estimatedVisitMinutes = 120 },
                new { attractionId = Park, name = "Tuileries", description = (string?)null, rating = (decimal?)null, address = "Place de la Concorde", lat = 48.86m, lon = 2.32m, estimatedVisitMinutes = 60 })),
            (5105, "/api/v1/transport-modes") => Json(new[] { new { transportModeId = Bus, code = "bus", name = "City bus", description = (string?)null } }),
            (5105, "/api/v1/route-points") => Json(Array.Empty<object>()),
            (5107, "/api/v1/settings") => Json(new { theme = "system", defaultMapLatitude = 0, defaultMapLongitude = 0, defaultMapZoom = 2, preferredTransportMode = "bus", timeZone = "UTC" }),
            (5108, "/api/v1/trips") when request.Method == HttpMethod.Post => await CreateAsync(request, cancellationToken),
            (5108, _) when request.Method == HttpMethod.Get && Guid.TryParse(path[(path.LastIndexOf('/') + 1)..], out var id) =>
                stored.TryGetValue(id, out var trip) ? Json(trip) : new HttpResponseMessage(HttpStatusCode.NotFound),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    /** the accommodation bodies the service received. */
    public List<string> AccommodationBodies { get; } = [];

    async Task<HttpResponseMessage> CreateAccommodationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        AccommodationBodies.Add(body);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var saved = new { accommodationId = Hotel, name = root.GetProperty("name").GetString(), address = root.GetProperty("address").GetString(), lat = root.GetProperty("lat").GetDecimal(), lon = root.GetProperty("lon").GetDecimal(), visibility = "private" };
        return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(saved) };
    }

    async Task<HttpResponseMessage> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        CreateBodies.Add(body);
        if (CreateGate is { } gate) await gate.Task;
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var legs = root.GetProperty("legs").EnumerateArray().Select(leg => new TripDto.Leg(
            leg.GetProperty("sequence").GetInt32(), Place(leg.GetProperty("from")), Place(leg.GetProperty("to")),
            new TripDto.Mode(Bus, "bus", "City bus"), leg.GetProperty("departure").GetDateTimeOffset(), leg.GetProperty("arrival").GetDateTimeOffset())).ToList();
        var trip = new TripDto(Guid.CreateVersion7(), root.GetProperty("name").GetString()!, root.GetProperty("timeZone").GetString()!,
            root.GetProperty("start").GetDateTimeOffset(), root.GetProperty("end").GetDateTimeOffset(), root.GetProperty("isRoundTrip").GetBoolean(), legs);
        stored[trip.TripId] = trip;
        return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(trip) };
    }

    static TripPlaceDto Place(JsonElement reference)
    {
        var id = reference.GetProperty("placeId").GetGuid();
        var type = reference.GetProperty("type").GetString()!;
        return id == Hotel ? new(id, type, "Hotel Lumiere", "1 Rue de Rivoli", 48.86m, 2.34m)
            : id == Museum ? new(id, type, "Louvre", "Rue de Rivoli", 48.86m, 2.33m)
            : new(id, type, "Tuileries", "Place de la Concorde", 48.86m, 2.32m);
    }

    static object[] Match(string name, params object[] items) =>
        items.Where(item => name.Length == 0 || item.GetType().GetProperty("name")!.GetValue(item)!.ToString()!.Contains(name, StringComparison.OrdinalIgnoreCase)).ToArray();

    static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
}
