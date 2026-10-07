using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Api.Tests;

/**
 * Verifies route-point creation, lookup and input validation through HTTP.
 */
public sealed class RoutePointTests
{
    /**
     * Creates each supported route-point type and reads its Location.
     * @param type One of the three permitted point roles.
     * @return Completion after checking creation and lookup.
     */
    [Theory]
    [InlineData("accommodation")]
    [InlineData("attraction")]
    [InlineData("transport_stop")]
    public async Task CreatePoint_SupportedTypeReturns201AndSavedLocation(string type)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var sourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type, name = " Example Point ", lat = -90m, lon = 180m, sourceId });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("routePointId").GetGuid());
        Assert.Equal(type, saved.GetProperty("type").GetString());
        Assert.Equal("Example Point", saved.GetProperty("name").GetString());
        Assert.Equal(sourceId, saved.GetProperty("sourceId").GetGuid());
        Assert.Equal(-90m, saved.GetProperty("lat").GetDecimal());
        Assert.Equal(180m, saved.GetProperty("lon").GetDecimal());
    }

    /**
     * Reads a seeded route point.
     * @return Completion after checking the seed.
     */
    [Fact]
    public async Task GetPointById_KnownPointReturnsSeed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/route-points/00000000-0000-0000-0000-000000000201");
        Assert.Equal("accommodation", saved.GetProperty("type").GetString());
        Assert.Equal("Example Hotel", saved.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("sourceId").ValueKind);
    }

    /**
     * Returns 404 for an absent route point.
     * @return Completion after checking the lookup response.
     */
    [Fact]
    public async Task GetPointById_UnknownPointReturns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/route-points/00000000-0000-0000-0000-000000000999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /**
     * Rejects an unsupported point role.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreatePoint_UnsupportedTypeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "restaurant", name = "Example", lat = 45m, lon = -73m });
        await HttpTestSupport.AssertValidationAsync(response);
    }

    /**
     * Rejects latitude outside the permitted coordinate range.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreatePoint_InvalidLatitudeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "transport_stop", name = "Example", lat = 90.1m, lon = -73m });
        await HttpTestSupport.AssertValidationAsync(response);
    }
}
