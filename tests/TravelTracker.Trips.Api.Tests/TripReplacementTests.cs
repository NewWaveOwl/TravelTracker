using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");
        var path = $"/api/v1/trips/{trip.TripId:D}";

        using var response = await owner.PutAsJsonAsync(path, TripTestData.OneWayBody(name: "Updated weekend"));
        using var reload = await owner.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reload.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
        Assert.Equal(response.Headers.ETag, reload.Headers.ETag);
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
        caller.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");

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
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");

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
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: "Denied"));
        using var missing = await owner.PutAsJsonAsync($"/api/v1/trips/{Guid.CreateVersion7():D}", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "banned");
        Assert.Same(trip, unchanged);
    }

    /** An invalid but bindable replacement still checks standing before returning field errors. */
    [Theory]
    [InlineData(false, "timeZone")]
    [InlineData(true, "legs[0]")]
    public async Task Replace_InvalidDraftChecksAccountStandingBeforeFieldErrors(bool nullLeg, string field)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var banned = factory.CreateClientFor("banned");
        using var owner = factory.CreateClientFor("alice");
        var path = $"/api/v1/trips/{trip.TripId:D}";
        banned.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");
        var body = JsonSerializer.SerializeToNode(TripTestData.OneWayBody())!.AsObject();
        if (nullLeg) body["legs"] = new JsonArray(new JsonNode?[] { null });
        else body["timeZone"] = "Unavailable/TimeZone";

        using var denied = await banned.PutAsJsonAsync(path, body);
        using var invalid = await owner.PutAsJsonAsync(path, body);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Same(trip, factory.Repository.Find(trip.TripId));
    }

    /** Two editors start from the same ETag and only the first save is accepted. */
    [Fact]
    public async Task Replace_StaleEditorCannotOverwriteCommittedTrip()
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var owner = factory.CreateClientFor("alice");
        using var created = await owner.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());
        var path = created.Headers.Location!;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var opened = await owner.GetAsync(path);
        Assert.Equal("\"1\"", created.Headers.ETag?.Tag);
        Assert.Equal(created.Headers.ETag, opened.Headers.ETag);

        using var missingTag = await owner.PutAsJsonAsync(path, TripTestData.OneWayBody(name: "Without revision"));
        Assert.Equal(HttpStatusCode.PreconditionRequired, missingTag.StatusCode);

        owner.DefaultRequestHeaders.IfMatch.Add(opened.Headers.ETag!);
        using var winner = await owner.PutAsJsonAsync(path, TripTestData.OneWayBody(name: "First save"));
        using var stale = await owner.PutAsJsonAsync(path, TripTestData.OneWayBody(name: "Stale editor"));

        Assert.Equal(HttpStatusCode.OK, winner.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using var reopened = await owner.GetAsync(path);
        var saved = await reopened.Content.ReadFromJsonAsync<TripResponse>();
        Assert.Equal("First save", saved!.Name);
        Assert.Equal(winner.Headers.ETag, reopened.Headers.ETag);
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
        owner.DefaultRequestHeaders.IfMatch.ParseAdd("\"1\"");

        using var response = await owner.PutAsJsonAsync($"/api/v1/trips/{trip.TripId:D}", TripTestData.OneWayBody(name: "Denied"));

        Assert.Equal(expectedStatus, response.StatusCode);
        var unchanged = await factory.Repository.GetOwnedByIdAsync(trip.TripId, TripsApiFactory.Issuer, "alice");
        Assert.Same(trip, unchanged);
    }
}
