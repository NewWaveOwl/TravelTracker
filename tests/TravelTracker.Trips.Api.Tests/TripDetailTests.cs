using System.Net;
using System.Text.Json;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/**
 * Proves own-trip retrieval: owners read their trips, every other caller gets the same absent response, and the
 * live standing rule decides what a banned or deactivated account may read.
 */
public sealed class TripDetailTests
{
    [Fact]
    public async Task Detail_OwnerReadsTheSnapshotTrip_WithoutOwnerIdentity()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor("alice");

        using var response = await client.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(trip.TripId, json.RootElement.GetProperty("tripId").GetGuid());
        Assert.Equal("Paris weekend", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("Europe/Paris", json.RootElement.GetProperty("timeZone").GetString());
        var leg = json.RootElement.GetProperty("legs")[0];
        Assert.Equal("Louvre", leg.GetProperty("to").GetProperty("name").GetString());
        Assert.Equal(48.85m, leg.GetProperty("from").GetProperty("lat").GetDecimal());
        Assert.DoesNotContain("ownerSubject", body);
        Assert.DoesNotContain("alice", body);
    }

    [Fact]
    public async Task Detail_AnotherUsersTrip_Returns404_LikeAnAbsentTrip()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var bobClient = factory.CreateClientFor("bob");

        using var response = await bobClient.GetAsync($"/api/v1/trips/{trip.TripId:D}");
        using var absent = await bobClient.GetAsync($"/api/v1/trips/{Guid.CreateVersion7():D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(absent.StatusCode, response.StatusCode);
    }

    [Fact]
    public async Task Detail_AdministratorWithoutOwnership_Returns404()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var adminClient = factory.CreateClientFor("admin");

        using var response = await adminClient.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_BannedOwner_MayReadOwnTrip()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "banned");
        using var client = factory.CreateClientFor("banned");

        using var response = await client.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Detail_DeactivatedOwner_Returns403()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "deactivated");
        using var client = factory.CreateClientFor("deactivated");

        using var response = await client.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Detail_UsersDown_Returns503()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        factory.Upstream.UsersUnavailable = true;
        using var client = factory.CreateClientFor("alice");

        using var response = await client.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Detail_MalformedIdentifier_Returns400()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        using var client = factory.CreateClientFor("alice");

        using var response = await client.GetAsync("/api/v1/trips/not-a-guid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
