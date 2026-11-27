using System.Net;
using System.Net.Http.Json;
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
    public async Task HistoryStatus_ValidatesOwnerAndStanding_PersistsAndResetsWithoutChangingItinerary()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        var bannedTrip = TripTestData.SeedTrip(factory, "banned");
        using var alice = factory.CreateClientFor("alice", "trips.write");
        using var reader = factory.CreateClientFor("alice", "trips.read");
        using var bob = factory.CreateClientFor("bob", "trips.write");
        using var banned = factory.CreateClientFor("banned", "trips.write");
        var path = $"/api/v1/trips/{trip.TripId:D}/status";
        var state = new { status = "upcoming" };

        using var privateTrip = await bob.PutAsJsonAsync(path, state);
        using var denied = await banned.PutAsJsonAsync($"/api/v1/trips/{bannedTrip.TripId:D}/status", state);
        using var readOnly = await reader.PutAsJsonAsync(path, state);
        Assert.Equal(HttpStatusCode.NotFound, privateTrip.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        foreach (var status in new[] { "upcoming", "current", "completed", "automatic" })
        {
            using var changed = await alice.PutAsJsonAsync(path, new { status });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            using var invalid = await alice.PutAsJsonAsync(path, new { status = "unknown" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var history = await reader.GetFromJsonAsync<JsonDocument>("/api/v1/trips");
            var summary = Assert.Single(history!.RootElement.EnumerateArray());
            Assert.Equal(status == "automatic" ? null : status, summary.GetProperty("status").GetString());
        }
        using var detail = await reader.GetAsync($"/api/v1/trips/{trip.TripId:D}");
        Assert.Equal($"\"{trip.Revision}\"", detail.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task History_ReturnsOnlyOwnedSummaries_AndRequiresReadAccess()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var aliceTrip = TripTestData.SeedTrip(factory, "alice");
        TripTestData.SeedTrip(factory, "bob");
        using var client = factory.CreateClientFor("alice", "trips.read");
        using var denied = factory.CreateClientFor("alice", "trips.write");
        using var deactivated = factory.CreateClientFor("deactivated", "trips.read");

        using var response = await client.GetAsync("/api/v1/trips");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var summary = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal(aliceTrip.TripId, summary.GetProperty("tripId").GetGuid());
        Assert.Equal(aliceTrip.Name, summary.GetProperty("name").GetString());
        Assert.Equal(aliceTrip.Start, summary.GetProperty("start").GetDateTimeOffset());
        Assert.Equal(aliceTrip.End, summary.GetProperty("end").GetDateTimeOffset());
        Assert.False(summary.TryGetProperty("legs", out _));
        Assert.False(summary.TryGetProperty("ownerSubject", out _));

        using var wrongScope = await denied.GetAsync("/api/v1/trips");
        using var wrongStanding = await deactivated.GetAsync("/api/v1/trips");
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrongStanding.StatusCode);
    }

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
        Assert.Equal(await ProblemBodies.WithoutTraceIdAsync(absent), await ProblemBodies.WithoutTraceIdAsync(response));
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
