using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/**
 * Proves the create endpoint's status mapping, the saved shape, the owner taken from the token, and that nothing is
 * stored whenever a check or a write fails.
 */
public sealed class TripCreationTests
{
    [Fact]
    public async Task Create_ValidTrip_Returns201_WithLocation_AndOwnerScopedSnapshot()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        var tripId = json.RootElement.GetProperty("tripId").GetGuid();
        Assert.Equal($"/api/v1/trips/{tripId:D}", response.Headers.Location?.AbsolutePath);
        Assert.Equal("Hotel Lumiere", json.RootElement.GetProperty("legs")[0].GetProperty("from").GetProperty("name").GetString());
        Assert.Equal("train", json.RootElement.GetProperty("legs")[0].GetProperty("transport").GetProperty("code").GetString());
        Assert.DoesNotContain("ownerSubject", body);
        Assert.DoesNotContain("alice", body);

        Assert.NotNull(await factory.Repository.GetOwnedByIdAsync(tripId, TripsApiFactory.Issuer, "alice"));
    }

    [Fact]
    public async Task Create_CatalogueCallsCarryCallerToken_AndUsersCallsCarryBackendCredential()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");
        var callerToken = client.DefaultRequestHeaders.Authorization!.ToString();

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(callerToken, factory.Upstream.Credentials[$"GET /api/v1/accommodations/{TripTestData.Hotel:D}"]);
        Assert.Equal(callerToken, factory.Upstream.Credentials[$"GET /api/v1/attractions/{TripTestData.Museum:D}"]);
        Assert.Equal("Bearer " + TripsApiFactory.BackendCredential, factory.Upstream.Credentials["POST /internal/users/identity"]);
        Assert.DoesNotContain(factory.Upstream.Credentials.Keys, key => key.Contains("/internal/") && factory.Upstream.Credentials[key] == callerToken);
    }

    [Fact]
    public async Task Create_NullLegEntry_Returns400WithFieldError_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");
        var body = new { name = "Paris weekend", timeZone = "Europe/Paris", start = TripTestData.Start, end = TripTestData.Start.AddHours(4), isRoundTrip = false, legs = new object?[] { null } };

        using var response = await client.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("legs[0]", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("Bearer")]
    public async Task Create_AcceptsAnySchemeCasing_AndForwardsTheSameToken(string scheme)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClient();
        var token = factory.CreateToken();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"{scheme} {token}");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Bearer " + token, factory.Upstream.Credentials[$"GET /api/v1/attractions/{TripTestData.Museum:D}"]);
    }

    [Theory]
    [InlineData("lat")]
    [InlineData("lon")]
    [InlineData("name")]
    [InlineData("address")]
    public async Task Create_AttractionAnswerMissingAField_FailsClosedInsteadOfSnapshottingDefaults(string missing)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var fields = new Dictionary<string, object> { ["name"] = "Louvre", ["address"] = "Rue de Rivoli", ["lat"] = 48.86m, ["lon"] = 2.34m };
        fields.Remove(missing);
        factory.Upstream.Respond("GET", $"/api/v1/attractions/{TripTestData.Museum:D}", HttpStatusCode.OK, fields);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData("lat")]
    [InlineData("lon")]
    public async Task Create_AccommodationAnswerMissingCoordinates_FailsClosed(string missing)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var fields = new Dictionary<string, object> { ["name"] = "Hotel Lumiere", ["address"] = "1 rue de Rivoli", ["lat"] = 48.85m, ["lon"] = 2.35m };
        fields.Remove(missing);
        factory.Upstream.Respond("GET", $"/api/v1/accommodations/{TripTestData.Hotel:D}", HttpStatusCode.OK, fields);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Theory]
    [InlineData("banned")]
    [InlineData("deactivated")]
    [InlineData("unknown")]
    public async Task Create_RefusedAccount_Returns403_BeforeAnyCatalogueLookup(string subject)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor(subject);

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(factory.Upstream.Calls, call => call.Contains("/api/v1/"));
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_UsersDown_Returns503_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Upstream.UsersUnavailable = true;
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("nothing was saved", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_CatalogueUnavailable_Returns503_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Upstream.Respond("GET", $"/api/v1/attractions/{TripTestData.Museum:D}", HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_AbsentReference_Returns404_WithFieldError_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Upstream.Respond("GET", $"/api/v1/accommodations/{TripTestData.Hotel:D}", HttpStatusCode.NotFound);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("legs[0].from", out _));
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_AnotherUsersPrivateReference_LooksAbsent_Returns404()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Upstream.Respond("GET", $"/api/v1/attractions/{TripTestData.Museum:D}", HttpStatusCode.Forbidden);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("legs[0].to", out _));
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_ItineraryBreaksTripRule_Returns400_WithFieldError_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");
        var body = new
        {
            name = "Hotel to hotel",
            timeZone = "Europe/Paris",
            start = TripTestData.Start,
            end = TripTestData.Start.AddHours(4),
            isRoundTrip = false,
            legs = new object[]
            {
                new
                {
                    sequence = 1,
                    from = new { placeId = TripTestData.Hotel, type = "accommodation" },
                    to = new { placeId = TripTestData.Hotel, type = "accommodation" },
                    transportModeId = TripTestData.Train,
                    departure = TripTestData.Start.AddHours(1),
                    arrival = TripTestData.Start.AddHours(2)
                }
            }
        };

        using var response = await client.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("legs[0].to", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_UnknownTimeZone_Returns400_BeforeAnyLookup()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody(timeZone: "Mars/Olympus"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeZone", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Upstream.Calls);
    }

    [Theory]
    [InlineData("ownerSubject")]
    [InlineData("tripId")]
    public async Task Create_ServerOwnedOrUnknownField_Returns400(string field)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice");
        var body = JsonSerializer.SerializeToNode(TripTestData.OneWayBody())!.AsObject();
        body[field] = "bob";

        using var response = await client.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Create_MissingLegs_Returns400()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        using var client = factory.CreateClientFor("alice");
        var body = new { name = "No legs", timeZone = "Europe/Paris", start = TripTestData.Start, end = TripTestData.Start.AddHours(4), isRoundTrip = false };

        using var response = await client.PostAsJsonAsync("/api/v1/trips", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_StorageFailure_Returns503_WithoutLeakingDetail_AndStoresNothing()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        factory.Repository.NextWriteFailure = new DbUpdateException("simulated write failure with host=secret", new InvalidOperationException());
        using var client = factory.CreateClientFor("alice");

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("nothing was saved", body);
        Assert.DoesNotContain("secret", body);
        Assert.Equal(0, factory.Repository.Count);
    }
}
