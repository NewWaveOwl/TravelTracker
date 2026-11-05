using System.Net;
using System.Net.Http.Json;
using TravelTracker.Accommodations.Api.Tests.Support;

namespace TravelTracker.Accommodations.Api.Tests;

/** Verifies explicit read/write scopes and production JWT validation over HTTP. */
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("catalogue.write", 403)]
    [InlineData("catalogue.read", 200)]
    public async Task Collection_RequiresCatalogueReadScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync("/api/v1/accommodations");

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("catalogue.read", 403)]
    [InlineData("catalogue.write", 201)]
    public async Task Create_RequiresCatalogueWriteScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Scope Matrix Inn",
            lat = 45m,
            lon = -73m
        });

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData("", 403)]
    [InlineData("catalogue.write", 403)]
    [InlineData("catalogue.read", 200)]
    public async Task Detail_RequiresCatalogueReadScope(string? scopes, int status)
    {
        using var factory = new ApiFactory();
        var id = await AccommodationTestData.SeedAsync(factory, "Visible to Alice", "alice", shared: false);
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync($"/api/v1/accommodations/{id:D}");

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
    public async Task ProtectedCollection_RejectsInvalidBearerTokens(string failure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateToken(failure: failure));

        using var response = await client.GetAsync("/api/v1/accommodations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Users!.Calls);
    }
}
