using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies saved schedules, UTC normalization and computed preparation/free windows.
 */
public sealed class CreateTransportRouteHttpTests
{
    [Fact]
    public async Task Create_MultipleStopsExposeDwellAndOnlyPermittedPotentialWindows()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", TransportRequests.Route());
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal("Scheduled Bus", saved.GetProperty("name").GetString());
        Assert.Equal("bus", saved.GetProperty("transport").GetProperty("code").GetString());
        Assert.Equal("accommodation", saved.GetProperty("start").GetProperty("type").GetString());
        Assert.Equal("attraction", saved.GetProperty("destination").GetProperty("type").GetString());
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero), saved.GetProperty("departureAt").GetDateTimeOffset());
        Assert.Equal(TimeSpan.Zero, saved.GetProperty("arrivalAt").GetDateTimeOffset().Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 11, 30, 0, TimeSpan.Zero), saved.GetProperty("boardingStartsAt").GetDateTimeOffset());
        Assert.Equal(480, saved.GetProperty("elapsedMinutes").GetDouble());
        Assert.Equal(510, saved.GetProperty("totalMinutes").GetDouble());
        var stops = saved.GetProperty("stops");
        Assert.Equal(2, stops.GetArrayLength());
        Assert.Equal(90, stops[0].GetProperty("downtimeMinutes").GetDouble());
        Assert.Equal(70, stops[0].GetProperty("potentialFreeMinutes").GetDouble());
        Assert.Equal(80, stops[1].GetProperty("downtimeMinutes").GetDouble());
        Assert.Equal(0, stops[1].GetProperty("potentialFreeMinutes").GetDouble());
        Assert.Equal(2, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000101", "bus")]
    [InlineData("00000000-0000-0000-0000-000000000102", "train")]
    [InlineData("00000000-0000-0000-0000-000000000103", "pedestrian")]
    [InlineData("00000000-0000-0000-0000-000000000104", "bike")]
    [InlineData("00000000-0000-0000-0000-000000000105", "plane")]
    [InlineData("00000000-0000-0000-0000-000000000106", "car")]
    public async Task Create_DirectOvernightRouteWorksForEveryMode(string id, string code)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route();
        request["transportModeId"] = id; request["stops"] = new JsonArray();
        request["departureAt"] = "2026-10-10T23:50:00-04:00";
        request["arrivalAt"] = "2026-10-11T00:20:00-04:00";
        request["boardingMinutes"] = 20;
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal(code, saved.GetProperty("transport").GetProperty("code").GetString());
        Assert.Equal(30, saved.GetProperty("elapsedMinutes").GetDouble());
        Assert.Equal(50, saved.GetProperty("totalMinutes").GetDouble());
        Assert.Empty(saved.GetProperty("stops").EnumerateArray());
    }
}
