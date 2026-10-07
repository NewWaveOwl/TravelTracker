using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Proves invalid schedules and missing references do not insert partial routes.
 */
public sealed class TransportRouteValidationHttpTests
{
    [Theory]
    [InlineData("name", """ "   " """)]
    [InlineData("boardingMinutes", "-1")]
    [InlineData("boardingMinutes", "null")]
    [InlineData("departureAt", "\"2026-10-10T08:00:00\"")]
    [InlineData("departureAt", "\"2026-10-10T8:00:00-04:00\"")]
    [InlineData("boardingMinutes", "2147483647")]
    [InlineData("departureAt", "\"not-a-date\"")]
    [InlineData("departureAt", "12")]
    [InlineData("arrivalAt", "\"2026-10-10T08:00:00-04:00\"")]
    [InlineData("arrivalAt", "\"2026-10-10T07:00:00-04:00\"")]
    [InlineData("stops", "null")]
    [InlineData("stops", "[null]")]
    [InlineData("transportModeId", "\"00000000-0000-0000-0000-000000000000\"")]
    [InlineData("transportRouteId", "\"00000000-0000-0000-0000-000000000001\"")]
    public async Task Create_InvalidRouteFieldDoesNotWrite(string field, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route(); request[field] = JsonNode.Parse(json);
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    [Theory]
    [InlineData("arrivalAt", "\"2026-10-10T08:00:00-04:00\"")]
    [InlineData("departureAt", "\"2026-10-10T08:30:00-04:00\"")]
    [InlineData("departureAt", "\"2026-10-10T16:00:00-04:00\"")]
    [InlineData("boardingMinutes", "91")]
    [InlineData("boardingMinutes", "-1")]
    [InlineData("boardingMinutes", "null")]
    [InlineData("canLeaveVehicle", "null")]
    [InlineData("pointId", "null")]
    public async Task Create_InvalidStopFieldDoesNotWrite(string field, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route(); request["stops"]![0]![field] = JsonNode.Parse(json);
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    [Fact]
    public async Task Create_OutOfOrderStopsAreRejectedWithoutReordering()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route();
        var stops = (JsonArray)request["stops"]!;
        var first = stops[0]!.DeepClone(); var second = stops[1]!.DeepClone();
        request["stops"] = new JsonArray(second, first);
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    [Theory]
    [InlineData("transportModeId")]
    [InlineData("startPointId")]
    [InlineData("destinationPointId")]
    [InlineData("stop")]
    public async Task Create_UnknownReferenceReturns404AndDoesNotWrite(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route();
        const string missing = "00000000-0000-0000-0000-000000000999";
        if (field == "stop") request["stops"]![0]!["pointId"] = missing;
        else request[field] = missing;
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }
}
