using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies transport entries can be created independently of routes.
 */
public sealed class TransportModeHttpTests
{
    [Theory]
    [InlineData("car")]
    [InlineData("bus")]
    [InlineData("train")]
    [InlineData("pedestrian")]
    [InlineData("bike")]
    [InlineData("plane")]
    public async Task Create_SupportedModeDoesNotCreateASchedule(string code)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var routesBefore = await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes");
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code, name = " New Transport ", description = "A manual entry." });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal("New Transport", saved.GetProperty("name").GetString());
        Assert.Equal(code, saved.GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, saved.GetProperty("transportModeId").GetGuid());
        Assert.Equal(7, await HttpTestSupport.CountAsync(client, "/api/v1/transport-modes"));
        Assert.Equal(routesBefore, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    [Theory]
    [InlineData("{\"code\":\"rocket\",\"name\":\"Test\"}")]
    [InlineData("{\"code\":\"bus\",\"name\":\" \"}")]
    [InlineData("{\"code\":\"bus\"}")]
    [InlineData("{\"code\":\"bus\",\"name\":\"Test\",\"transportModeId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"code\":\"bus\",\"name\":\"Test\",\"departureAt\":\"2026-10-10T00:00:00Z\"}")]
    public async Task Create_InvalidEntryDoesNotWrite(string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/transport-modes", HttpTestSupport.Json(json));
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(6, await HttpTestSupport.CountAsync(client, "/api/v1/transport-modes"));
    }
}
