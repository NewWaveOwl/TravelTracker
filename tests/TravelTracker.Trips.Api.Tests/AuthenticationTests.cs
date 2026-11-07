using System.Net;
using System.Net.Http.Json;
using TravelTracker.Trips.Api.Tests.Support;

namespace TravelTracker.Trips.Api.Tests;

/** Verifies explicit trip read and write scopes, real JWT validation and the public health endpoint. */
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("trips.write", 403)]
    [InlineData("catalogue.read", 403)]
    [InlineData("trips.read", 200)]
    public async Task Detail_RequiresTripsReadScope(string? scopes, int status)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        var trip = TripTestData.SeedTrip(factory, "alice");
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync($"/api/v1/trips/{trip.TripId:D}");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("trips.read", 403)]
    [InlineData("catalogue.write", 403)]
    [InlineData("trips.write", 201)]
    public async Task Create_RequiresTripsWriteScope(string? scopes, int status)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("unsigned")]
    public async Task ProtectedRoutes_RejectInvalidBearerTokensBeforeAnyLookup(string failure)
    {
        using var factory = new TripsApiFactory();
        TripTestData.SeedAccounts(factory.Upstream);
        TripTestData.SeedHealthyCatalogue(factory.Upstream);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(failure: failure));

        using var detail = await client.GetAsync($"/api/v1/trips/{TripTestData.Hotel:D}");
        using var create = await client.PostAsJsonAsync("/api/v1/trips", TripTestData.OneWayBody());

        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.Empty(factory.Upstream.Calls);
        Assert.Equal(0, factory.Repository.Count);
    }

    [Fact]
    public async Task Health_IsAnonymous_AndCallsNoDependency()
    {
        using var factory = new TripsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"service\":\"Trips\"", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Upstream.Calls);
    }
}
