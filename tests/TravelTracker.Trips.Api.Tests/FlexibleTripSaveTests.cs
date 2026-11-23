using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/** Exercises category-independent saves and the additive empty/single-stop contract through signed HTTP requests. */
public sealed class FlexibleTripSaveTests
{
    private static readonly Guid Station = Guid.Parse("0196a1b2-0000-7000-8000-000000000003");

    [Theory]
    [InlineData("accommodation", "accommodation")]
    [InlineData("accommodation", "attraction")]
    [InlineData("accommodation", "transport_stop")]
    [InlineData("attraction", "accommodation")]
    [InlineData("attraction", "attraction")]
    [InlineData("attraction", "transport_stop")]
    [InlineData("transport_stop", "accommodation")]
    [InlineData("transport_stop", "attraction")]
    [InlineData("transport_stop", "transport_stop")]
    public async Task Create_AllSupportedEndpointCompositions_SaveWithoutRequiredVisits(string fromType, string toType)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = JourneyBody(fromType, toType);

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<TripResponse>();
        Assert.NotNull(saved);
        var leg = Assert.Single(saved.Legs);
        Assert.Equal(fromType, leg.From.Type);
        Assert.Equal(toType, leg.To.Type);
        Assert.Equal(new[] { leg.From, leg.To }, saved.Stops);
        Assert.Equal($"/api/v1/trips/{saved.TripId:D}", response.Headers.Location?.AbsolutePath);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Create_RoundTripIntentWithoutReturnOrAttraction_PreservesSuppliedJourney()
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = JourneyBody("transport_stop", "accommodation");
        body["isRoundTrip"] = true;

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.True(saved.IsRoundTrip);
        Assert.Single(saved.Legs);
        Assert.Equal(Station, saved.Stops[0].PlaceId);
        Assert.Equal(TripTestData.Hotel, saved.Stops[1].PlaceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_EmptyTrip_IsSavedAndReloadedWithoutCatalogueLookups(bool roundTrip)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = EmptyBody(roundTrip);

        using var created = await owner.PostAsJsonAsync("/api/v1/trips", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var reloaded = await owner.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var saved = (await reloaded.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal(roundTrip, saved.IsRoundTrip);
        Assert.Empty(saved.Legs);
        Assert.Empty(saved.Stops);
        Assert.Equal(created.Headers.ETag, reloaded.Headers.ETag);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
    }

    [Theory]
    [InlineData("accommodation", false)]
    [InlineData("attraction", false)]
    [InlineData("transport_stop", false)]
    [InlineData("accommodation", true)]
    [InlineData("attraction", true)]
    [InlineData("transport_stop", true)]
    public async Task Create_SingleStop_SnapshotsTheSelectedTypeWithoutInventingAJourney(string type, bool roundTrip)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();

        using var created = await owner.PostAsJsonAsync("/api/v1/trips", SingleStopBody(type, roundTrip));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var reloaded = await owner.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var saved = (await reloaded.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Empty(saved.Legs);
        Assert.Equal(roundTrip, saved.IsRoundTrip);
        var stop = Assert.Single(saved.Stops);
        Assert.Equal(IdFor(type), stop.PlaceId);
        Assert.Equal(type, stop.Type);
        Assert.NotEmpty(stop.Name);
        Assert.Equal(1, factory.Upstream.Calls.Count(call => call.Contains("/api/v1/")));
        Assert.Equal(owner.DefaultRequestHeaders.Authorization!.ToString(), factory.Upstream.Credentials[$"GET {PathFor(type)}"]);
    }

    [Fact]
    public async Task Create_ExplicitMatchingStops_UsesTheResolvedLegSnapshotsWithoutExtraLookups()
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = JourneyBody("accommodation", "attraction");
        body["stops"] = new JsonArray(Reference("accommodation"), Reference("attraction"));

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal(2, saved.Stops.Count);
        Assert.Equal(3, factory.Upstream.Calls.Count(call => call.Contains("/api/v1/")));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("reversed")]
    [InlineData("extra")]
    public async Task Create_StopsDisagreeWithLegs_Reports400BeforeCatalogueLookups(string mismatch)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = JourneyBody("accommodation", "attraction");
        body["stops"] = mismatch switch
        {
            "empty" => new JsonArray(),
            "reversed" => new JsonArray(Reference("attraction"), Reference("accommodation")),
            _ => new JsonArray(Reference("accommodation"), Reference("attraction"), Reference("transport_stop"))
        };

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("stops", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
        Assert.Contains(factory.Upstream.Calls, call => call.StartsWith("POST /internal/users/identity", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    public async Task Create_RestrictedAccountWithMismatchedStops_Is403BeforeShapeErrors(string subject)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor(subject: subject);
        var body = JourneyBody("accommodation", "attraction");
        body["stops"] = new JsonArray();

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("stops", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/", StringComparison.Ordinal));
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData("null-entry")]
    [InlineData("missing-id")]
    [InlineData("empty-id")]
    [InlineData("missing-type")]
    [InlineData("unsupported-type")]
    [InlineData("snapshot-field")]
    [InlineData("multiple-without-legs")]
    public async Task Create_InvalidStopInput_Is400AndWritesNothing(string invalid)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = SingleStopBody("attraction");
        var stops = body["stops"]!.AsArray();
        switch (invalid)
        {
            case "null-entry": stops[0] = null; break;
            case "missing-id": stops[0]!.AsObject().Remove("placeId"); break;
            case "empty-id": stops[0]!["placeId"] = Guid.Empty; break;
            case "missing-type": stops[0]!.AsObject().Remove("type"); break;
            case "unsupported-type": stops[0]!["type"] = "hotel"; break;
            case "snapshot-field": stops[0]!["lat"] = 48.86; break;
            default: stops.Add(Reference("accommodation")); break;
        }

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("stops", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.Repository.Count);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
    }

    [Theory]
    [InlineData("accommodation", 91, 2)]
    [InlineData("accommodation", 48, -181)]
    [InlineData("attraction", -91, 2)]
    [InlineData("attraction", 48, 181)]
    [InlineData("transport_stop", 91, 2)]
    [InlineData("transport_stop", 48, -181)]
    public async Task Create_InvalidUpstreamStopCoordinates_FailsClosedWithoutSaving(string type, int latitude, int longitude)
    {
        using var factory = CreateFactory();
        factory.Upstream.Respond("GET", PathFor(type), HttpStatusCode.OK,
            new { type, name = "Invalid catalogue place", address = "", lat = latitude, lon = longitude });
        using var owner = factory.CreateClientFor();

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", SingleStopBody(type));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData("accommodation")]
    [InlineData("attraction")]
    [InlineData("transport_stop")]
    public async Task Create_InvalidUpstreamLegCoordinates_FailsClosedWithoutSaving(string type)
    {
        using var factory = CreateFactory();
        factory.Upstream.Respond("GET", PathFor(type), HttpStatusCode.OK,
            new { type, name = "Invalid catalogue place", address = "", lat = -91, lon = 2 });
        using var owner = factory.CreateClientFor();

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", JourneyBody(type, "attraction"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("reversed-dates")]
    [InlineData("missing-start")]
    [InlineData("missing-end")]
    [InlineData("unknown-zone")]
    public async Task Create_EmptyTripStillValidatesItsBasics(string invalid)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = EmptyBody();
        var field = invalid switch
        {
            "name" => "name",
            "missing-start" => "start",
            "unknown-zone" => "timeZone",
            _ => "end"
        };
        if (invalid is "missing-start" or "missing-end") body.Remove(field);
        else body[field] = invalid switch
        {
            "name" => JsonValue.Create(" "),
            "unknown-zone" => JsonValue.Create("Unknown/TimeZone"),
            _ => JsonValue.Create(TripTestData.Start.AddHours(-1))
        };

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Create_UtcTripBoundsWithParisTimeZone_PreservesEnteredOffsetsOnReload(bool withJourney)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        var body = withJourney ? JourneyBody("accommodation", "attraction") : EmptyBody();
        var start = TripTestData.Start.ToOffset(TimeSpan.Zero);
        var end = TripTestData.Start.AddHours(4).ToOffset(TimeSpan.Zero);
        body["start"] = start;
        body["end"] = end;

        using var created = await owner.PostAsJsonAsync("/api/v1/trips", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var reloaded = await owner.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var saved = (await reloaded.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal("Europe/Paris", saved.TimeZone);
        Assert.Equal(start, saved.Start);
        Assert.Equal(end, saved.End);
        Assert.Equal(TimeSpan.Zero, saved.Start.Offset);
        Assert.Equal(TimeSpan.Zero, saved.End.Offset);
        if (withJourney)
        {
            var leg = Assert.Single(saved.Legs);
            Assert.Equal(TimeSpan.FromHours(2), leg.Departure.Offset);
            Assert.Equal(TimeSpan.FromHours(2), leg.Arrival.Offset);
        }
        else Assert.Empty(saved.Legs);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    [InlineData("unknown")]
    public async Task Create_EmptyTripStillRequiresLiveActiveStanding(string subject)
    {
        using var factory = CreateFactory();
        using var caller = factory.CreateClientFor(subject);

        using var response = await caller.PostAsJsonAsync("/api/v1/trips", EmptyBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("trips.write", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.read", HttpStatusCode.Forbidden)]
    public async Task Create_EmptyTripKeepsAuthenticationAndWriteScopeRequirements(string? scopes, HttpStatusCode expected)
    {
        using var factory = CreateFactory();
        using var caller = factory.CreateClientFor(scopes: scopes);

        using var response = await caller.PostAsJsonAsync("/api/v1/trips", EmptyBody());

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
        Assert.Empty(factory.Upstream.Calls);
    }

    [Fact]
    public async Task Create_EmptyTripStillFailsClosedWhenUsersIsUnavailable()
    {
        using var factory = CreateFactory();
        factory.Upstream.UsersUnavailable = true;
        using var owner = factory.CreateClientFor();

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", EmptyBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Create_InaccessibleSingleStop_LooksAbsentAndStoresNothing(HttpStatusCode upstreamStatus)
    {
        using var factory = CreateFactory();
        factory.Upstream.Respond("GET", PathFor("attraction"), upstreamStatus);
        using var owner = factory.CreateClientFor();

        using var response = await owner.PostAsJsonAsync("/api/v1/trips", SingleStopBody("attraction"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("stops[0]", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_JourneyWithEmptyOrSingleStop_ReloadsNewSnapshotAndAdvancesRevision(bool singleStop)
    {
        using var factory = CreateFactory();
        var original = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor();
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");
        var path = $"/api/v1/trips/{original.TripId:D}";
        var body = singleStop ? SingleStopBody("transport_stop", true) : EmptyBody(true);

        using var response = await owner.PutAsJsonAsync(path, body);
        using var reloaded = await owner.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var saved = (await reloaded.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal(original.TripId, saved.TripId);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
        Assert.Equal(response.Headers.ETag, reloaded.Headers.ETag);
        Assert.Empty(saved.Legs);
        Assert.Equal(singleStop ? 1 : 0, saved.Stops.Count);
        if (singleStop) Assert.Equal(Station, saved.Stops[0].PlaceId);
        Assert.Equal(1, factory.Repository.Count);
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task Replace_WithSingleStopCannotOverwriteAnotherTravellersTrip(string subject)
    {
        using var factory = CreateFactory();
        var original = TripTestData.SeedTrip(factory, "alice");
        using var caller = factory.CreateClientFor(subject);
        caller.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");

        using var denied = await caller.PutAsJsonAsync($"/api/v1/trips/{original.TripId:D}", SingleStopBody("attraction"));
        using var absent = await caller.PutAsJsonAsync($"/api/v1/trips/{Guid.Parse("0196a1b2-ffff-7000-8000-000000000001"):D}", SingleStopBody("attraction"));

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(denied.StatusCode, absent.StatusCode);
        Assert.Same(original, factory.Repository.Find(original.TripId));
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Get_EmptyOrSingleStopTripIsHiddenAcrossSubjectsAndIssuers(bool singleStop)
    {
        using var factory = CreateFactory();
        using var owner = factory.CreateClientFor();
        using var created = await owner.PostAsJsonAsync("/api/v1/trips", singleStop ? SingleStopBody("attraction") : EmptyBody());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = (await created.Content.ReadFromJsonAsync<TripResponse>())!;
        using var other = factory.CreateClientFor("bob");
        using var denied = await other.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Null(await factory.Repository.GetOwnedByIdAsync(saved.TripId, "https://other-issuer.example/", "alice"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replace_InvalidOrUnavailableSingleStop_LeavesOldSnapshotAndRevisionUntouched(bool unavailable)
    {
        using var factory = CreateFactory();
        var original = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor();
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");
        var body = SingleStopBody("attraction");
        if (unavailable) factory.Upstream.Respond("GET", PathFor("attraction"), HttpStatusCode.ServiceUnavailable);
        else body["end"] = TripTestData.Start.AddHours(-1);

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{original.TripId:D}", body);

        Assert.Equal(unavailable ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Same(original, factory.Repository.Find(original.TripId));
        Assert.Equal(1, factory.Repository.Count);
    }

    /** @return An isolated signed-JWT host with active accounts and all three supported place categories. */
    private static TripsApiFactory CreateFactory()
    {
        var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Upstream.Respond("GET", PathFor("transport_stop"), HttpStatusCode.OK,
            new { routePointId = Station, type = "transport_stop", name = "Gare du Nord", address = "Paris", lat = 48.88m, lon = 2.35m });
        return factory;
    }

    /** @param type Supported category. @return The catalogue identifier for that test place. */
    private static Guid IdFor(string type) => type switch
    {
        "accommodation" => TripTestData.Hotel,
        "attraction" => TripTestData.Museum,
        _ => Station
    };

    /** @param type Supported category. @return Its owning service's detail path. */
    private static string PathFor(string type) => type switch
    {
        "accommodation" => $"/api/v1/accommodations/{TripTestData.Hotel:D}",
        "attraction" => $"/api/v1/attractions/{TripTestData.Museum:D}",
        _ => $"/api/v1/route-points/{Station:D}"
    };

    /** @param type Supported category. @return A new caller-writable public place reference. */
    private static JsonObject Reference(string type) => new() { ["placeId"] = IdFor(type), ["type"] = type };

    /** @param roundTrip Traveller's round-trip intent. @return The minimum named, dated request with no places. */
    private static JsonObject EmptyBody(bool roundTrip = false) => JsonSerializer.SerializeToNode(new
    {
        name = "Flexible itinerary", timeZone = "Europe/Paris", start = TripTestData.Start,
        end = TripTestData.Start.AddHours(4), isRoundTrip = roundTrip, legs = Array.Empty<object>()
    })!.AsObject();

    /** @param type Supported category. @param roundTrip Traveller's round-trip intent. @return A lone stop without a transport leg. */
    private static JsonObject SingleStopBody(string type, bool roundTrip = false)
    {
        var body = EmptyBody(roundTrip);
        body["stops"] = new JsonArray(Reference(type));
        return body;
    }

    /** @param fromType Departure category. @param toType Arrival category. @return One valid journey in the chosen categories. */
    private static JsonObject JourneyBody(string fromType, string toType)
    {
        var body = EmptyBody();
        body["legs"] = new JsonArray(new JsonObject
        {
            ["sequence"] = 1, ["from"] = Reference(fromType), ["to"] = Reference(toType),
            ["transportModeId"] = TripTestData.Train, ["departure"] = TripTestData.Start.AddHours(1),
            ["arrival"] = TripTestData.Start.AddHours(2)
        });
        return body;
    }
}
