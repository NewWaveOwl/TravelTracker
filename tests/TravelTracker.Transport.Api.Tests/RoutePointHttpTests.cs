using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies typed locations and independent catalogue reference metadata.
 */
public sealed class RoutePointHttpTests
{
    [Theory]
    [InlineData("accommodation")]
    [InlineData("attraction")]
    [InlineData("transport_stop")]
    public async Task Create_AcceptsSupportedPointTypeAndCoordinateBoundaries(string type)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var source = Guid.Parse("00000000-0000-0000-0000-000000000999");
        using var response = await client.PostAsJsonAsync("/api/v1/route-points", new { type, name = " New Point ", lat = -90m, lon = 180m, sourceId = source });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal(type, saved.GetProperty("type").GetString());
        Assert.Equal(source, saved.GetProperty("sourceId").GetGuid());
        Assert.Equal("New Point", saved.GetProperty("name").GetString());
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
        Assert.Equal(4, await HttpTestSupport.CountAsync(client, "/api/v1/route-points"));
    }

    [Theory]
    [InlineData("type", "\"hotel\"")]
    [InlineData("name", """ "   " """)]
    [InlineData("lat", "90.1")]
    [InlineData("lon", "-180.1")]
    [InlineData("lat", "null")]
    [InlineData("address", "null")]
    [InlineData("sourceId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("routePointId", "\"00000000-0000-0000-0000-000000000001\"")]
    public async Task Create_InvalidLocationDoesNotWrite(string field, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = new JsonObject { ["type"] = "attraction", ["name"] = "Valid", ["lat"] = 0, ["lon"] = 0 };
        request[field] = JsonNode.Parse(json);
        using var response = await client.PostAsJsonAsync("/api/v1/route-points", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/route-points"));
    }
}
