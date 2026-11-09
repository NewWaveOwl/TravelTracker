using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Journey.Tests.Support;

namespace TravelTracker.Journey.Tests;

/**
 * runs the critical traveller journey across the real Settings, Accommodations, Attractions, Transport and Trips hosts.
 * every call goes through each host's production bearer handler, authorization policies, validation and PostgreSQL store.
 */
[Trait("Category", "PostgreSQL")]
public sealed class CriticalJourneyTests : IDisposable
{
    private const string TravellerScopes = "catalogue.read catalogue.write trips.read trips.write settings.read settings.write";

    private readonly JourneyEnvironment environment = new();

    /** stops the hosts and drops their databases. */
    public void Dispose() => environment.Dispose();

    /** a traveller saves settings, builds a catalogue, plans a trip and reads it back; another traveller sees none of it. */
    [Fact]
    public async Task Traveller_SavesSettingsAndPlansATrip_AndOtherTravellerSeesNothing()
    {
        using var settings = environment.ClientFor(environment.Settings, "alice", TravellerScopes);

        using var defaults = await settings.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        var tag = defaults.Headers.ETag!.Tag;
        var current = await defaults.Content.ReadFromJsonAsync<JsonElement>();

        var changed = new Dictionary<string, object?>();
        foreach (var property in current.EnumerateObject()) changed[property.Name] = property.Value;
        changed["preferredTransportMode"] = "train";
        using var save = new HttpRequestMessage(HttpMethod.Put, "/api/v1/settings") { Content = JsonContent.Create(changed) };
        save.Headers.TryAddWithoutValidation("If-Match", tag);
        using var saved = await settings.SendAsync(save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var reloaded = await settings.GetAsync("/api/v1/settings");
        Assert.Equal("train", (await reloaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preferredTransportMode").GetString());

        using var bobSettings = environment.ClientFor(environment.Settings, "bob", TravellerScopes);
        using var bobSettingsRead = await bobSettings.GetAsync("/api/v1/settings");
        Assert.NotEqual("train", (await bobSettingsRead.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("preferredTransportMode").GetString());

        var hotel = await CreateAsync(environment.ClientFor(environment.Accommodations, "alice", TravellerScopes),
            "/api/v1/accommodations", new { name = "Hotel Lumiere", lat = 48.85m, lon = 2.35m }, "accommodationId");
        var museum = await CreateAsync(environment.ClientFor(environment.Attractions, "alice", TravellerScopes),
            "/api/v1/attractions", new { name = "Louvre", lat = 48.86m, lon = 2.34m, estimatedVisitMinutes = 120 }, "attractionId");
        var train = await CreateAsync(environment.ClientFor(environment.Transport, "alice", TravellerScopes),
            "/api/v1/transport-modes", new { code = "train", name = "Train" }, "transportModeId");

        var start = new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));
        using var trips = environment.ClientFor(environment.Trips, "alice", TravellerScopes);
        using var created = await trips.PostAsJsonAsync("/api/v1/trips", new
        {
            name = "Paris weekend",
            timeZone = "Europe/Paris",
            start,
            end = start.AddHours(4),
            isRoundTrip = false,
            legs = new object[]
            {
                new
                {
                    sequence = 1,
                    from = new { placeId = hotel, type = "accommodation" },
                    to = new { placeId = museum, type = "attraction" },
                    transportModeId = train,
                    departure = start.AddHours(1),
                    arrival = start.AddHours(2)
                }
            }
        });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var location = created.Headers.Location!;

        using var read = await trips.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var trip = await read.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Paris weekend", trip.GetProperty("name").GetString());
        Assert.Equal("Hotel Lumiere", trip.GetProperty("legs")[0].GetProperty("from").GetProperty("name").GetString());

        using var bobTrips = environment.ClientFor(environment.Trips, "bob", TravellerScopes);
        using var bobTripRead = await bobTrips.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, bobTripRead.StatusCode);
    }

    /** every host refuses a request with no token, a bad token or too little scope, and the public health route stays open. */
    [Fact]
    public async Task EveryHost_RefusesMissingInvalidAndUnderScopedTokens()
    {
        var routes = new (string Name, HttpClient Anonymous, string Path)[]
        {
            ("Settings", environment.Settings.CreateClient(), "/api/v1/settings"),
            ("Trips", environment.Trips.CreateClient(), $"/api/v1/trips/{Guid.NewGuid()}"),
            ("Accommodations", environment.Accommodations.CreateClient(), "/api/v1/accommodations"),
            ("Attractions", environment.Attractions.CreateClient(), "/api/v1/attractions"),
            ("Transport", environment.Transport.CreateClient(), "/api/v1/transport-modes")
        };

        foreach (var (name, anonymous, path) in routes)
        {
            using (anonymous)
            {
                Assert.True(HttpStatusCode.Unauthorized == (await anonymous.GetAsync(path)).StatusCode, $"{name} must refuse a missing token");
                Assert.True(HttpStatusCode.OK == (await anonymous.GetAsync("/health")).StatusCode, $"{name} health must stay public");
            }
        }

        foreach (var failure in new[] { "audience", "expired", "signature" })
        {
            using var settings = environment.ClientFor(environment.Settings, "alice", TravellerScopes, failure);
            Assert.True(HttpStatusCode.Unauthorized == (await settings.GetAsync("/api/v1/settings")).StatusCode, $"a {failure} token must be refused");
        }

        using var underScoped = environment.ClientFor(environment.Settings, "alice", "catalogue.read");
        Assert.Equal(HttpStatusCode.Forbidden, (await underScoped.GetAsync("/api/v1/settings")).StatusCode);
    }

    /** live account standing from Users applies: banned accounts read but cannot write, deactivated accounts cannot do either. */
    [Fact]
    public async Task AccountStanding_IsEnforcedDespiteAValidToken()
    {
        using var banned = environment.ClientFor(environment.Settings, "banned", TravellerScopes);
        using var bannedRead = await banned.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.OK, bannedRead.StatusCode);
        using var bannedWrite = new HttpRequestMessage(HttpMethod.Put, "/api/v1/settings")
        {
            Content = JsonContent.Create(await bannedRead.Content.ReadFromJsonAsync<JsonElement>())
        };
        bannedWrite.Headers.TryAddWithoutValidation("If-Match", bannedRead.Headers.ETag!.Tag);
        using var bannedSave = await banned.SendAsync(bannedWrite);
        Assert.Equal(HttpStatusCode.Forbidden, bannedSave.StatusCode);

        using var deactivated = environment.ClientFor(environment.Settings, "deactivated", TravellerScopes);
        using var deactivatedRead = await deactivated.GetAsync("/api/v1/settings");
        Assert.Equal(HttpStatusCode.Forbidden, deactivatedRead.StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string path, object body, string idProperty)
    {
        using (client)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            Assert.True(response.StatusCode == HttpStatusCode.Created, $"{path}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty(idProperty).GetGuid();
        }
    }
}
