using System.Net;
using System.Net.Http.Json;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/** Verifies replacement through the public Trips contract with signed caller credentials. */
public sealed class TripReplacementTests
{
    [Fact]
    public async Task Replace_OwnerCanReloadReplacementWithSameIdentifier()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor("alice");
        var path = $"/api/v1/trips/{trip.TripId:D}";

        using var response = await owner.PutAsJsonAsync(path, TripTestData.OneWayBody(name: "Updated weekend"));
        using var reload = await owner.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        foreach (var result in new[] { response, reload })
        {
            var saved = await result.Content.ReadFromJsonAsync<TripResponse>();
            Assert.NotNull(saved);
            Assert.Equal(trip.TripId, saved.TripId);
            Assert.Equal("Updated weekend", saved.Name);
            Assert.Equal(TripTestData.Train, Assert.Single(saved.Legs).Transport.TransportModeId);
        }
        Assert.Equal(1, factory.Repository.Count);
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("admin")]
    public async Task Replace_OtherCallerGetsSameResponseAsMissingTrip(string subject)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var caller = factory.CreateClientFor(subject);

        using var denied = await caller.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: "Denied"));
        using var missing = await caller.PutAsJsonAsync($"/api/v1/trips/{Guid.CreateVersion7():D}", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Equal(denied.StatusCode, missing.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "alice");
        Assert.Same(trip, unchanged);
    }

    [Fact]
    public async Task Replace_InvalidTripLeavesSavedDraftUntouched()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor("alice");

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: " "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "alice");
        Assert.Same(trip, unchanged);
    }

    [Fact]
    public async Task Replace_BannedOwnerCannotChangeSavedTrip()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "banned");
        using var owner = factory.CreateClientFor("banned");

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: "Denied"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "banned");
        Assert.Same(trip, unchanged);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("trips.read catalogue.read", HttpStatusCode.Forbidden)]
    [InlineData("trips.write", HttpStatusCode.Forbidden)]
    public async Task Replace_RequiresSignedCallerWithWriteAndCatalogueScopes(string? scopes, HttpStatusCode expectedStatus)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var owner = factory.CreateClientFor(scopes: scopes);

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: "Denied"));

        Assert.Equal(expectedStatus, response.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "alice");
        Assert.Same(trip, unchanged);
    }
}
