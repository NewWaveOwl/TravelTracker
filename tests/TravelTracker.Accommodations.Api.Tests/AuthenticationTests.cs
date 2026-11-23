using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using TravelTracker.Accommodations.Api;
using TravelTracker.Accommodations.Api.Tests.Support;

namespace TravelTracker.Accommodations.Api.Tests;

/** Verifies explicit read/write scopes and production JWT validation over HTTP. */
public sealed class AuthenticationTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.write", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.read", HttpStatusCode.OK)]
    public async Task Collection_RequiresCatalogueReadScope(string? scopes, HttpStatusCode status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync("/api/v1/accommodations");

        Assert.Equal(status, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.read", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.write", HttpStatusCode.Created)]
    public async Task Create_RequiresCatalogueWriteScope(string? scopes, HttpStatusCode status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.PostAsJsonAsync("/api/v1/accommodations", new
        {
            name = "Scope Matrix Inn",
            lat = 45m,
            lon = -73m
        });

        Assert.Equal(status, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.write", HttpStatusCode.Forbidden)]
    [InlineData("catalogue.read", HttpStatusCode.OK)]
    public async Task Detail_RequiresCatalogueReadScope(string? scopes, HttpStatusCode status)
    {
        using var factory = new ApiFactory();
        var id = await AccommodationTestData.SeedAsync(factory, Guid.Parse("00000000-0000-0000-0000-000000000914"), "Visible to Alice", "alice", shared: false);
        using var client = factory.CreateClientFor("alice", scopes);

        using var response = await client.GetAsync($"/api/v1/accommodations/{id:D}");

        Assert.Equal(status, response.StatusCode);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("missing-sub")]
    [InlineData("empty-sub")]
    [InlineData("unsigned")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("no-expiry")]
    [InlineData("future")]
    public async Task ProtectedCollection_RejectsInvalidBearerTokens(string failure)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var token = failure == "missing" ? null : factory.CreateToken(failure: failure);
        client.DefaultRequestHeaders.Authorization = token is null ? null : new("Bearer", token);

        using var response = await client.GetAsync("/api/v1/accommodations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        if (token is not null)
        {
            Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain(token, string.Join(" ", response.Headers.WwwAuthenticate));
        }
        Assert.Equal(0, factory.Users!.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Health_RemainsPublicWithMissingOrInvalidToken(bool invalidToken)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = invalidToken ? new("Bearer", "invalid") : null;

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Development_ShippedConfigurationStartsWithPublicHealthAndProtectedCollection()
    {
        // no ApiFactory overrides: load the repository's shipped Development settings, no other services running.
        using var factory = new WebApplicationFactory<AccommodationsProgram>().WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var health = await client.GetAsync("/health");
        using var collection = await client.GetAsync("/api/v1/accommodations");
        using var swagger = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, collection.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
    }
}
