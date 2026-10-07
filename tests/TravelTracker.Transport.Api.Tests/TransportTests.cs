using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Transport.Api.Tests;

/**
 * Verifies the Transport service with LAB_07-style xUnit tests in one file.
 */
public sealed class TransportTests
{
    /**
     * Verifies independent service health.
     */
    [Fact]
    public async Task Health_ReturnsServiceIdentity()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var value = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", value.GetProperty("status").GetString());
        Assert.Equal("Transport", value.GetProperty("service").GetString());
    }

    /**
     * Verifies transport entries can be created independently of routes.
     */
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

    /**
     * Verifies typed locations and independent catalogue reference metadata.
     */
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

    /**
     * Verifies catalogue, point and route detail status contracts.
     */
    [Theory]
    [InlineData("transport-modes", "00000000-0000-0000-0000-000000000101")]
    [InlineData("route-points", "00000000-0000-0000-0000-000000000201")]
    [InlineData("transport-routes", "00000000-0000-0000-0000-000000000301")]
    public async Task Get_HandlesKnownUnknownAndMalformedIds(string resource, string known)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var found = await client.GetAsync($"/api/v1/{resource}/{known}");
        using var missing = await client.GetAsync($"/api/v1/{resource}/00000000-0000-0000-0000-000000000999");
        using var invalid = await client.GetAsync($"/api/v1/{resource}/not-a-guid");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await HttpTestSupport.AssertValidationAsync(invalid);
    }

    /**
     * Verifies saved schedules, UTC normalization and computed preparation/free windows.
     */
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

    /**
     * Proves invalid schedules and missing references do not insert partial routes.
     */
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

    /**
     * Verifies catalogue and schedule stores reset together in a fresh host.
     */
    [Fact]
    public async Task FreshHost_DiscardsCataloguePointAndRouteWrites()
    {
        var locations = new List<Uri>();
        using (var first = new ApiFactory())
        using (var client = first.CreateClient())
        {
            using var mode = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bus", name = "Extra" });
            await HttpTestSupport.AssertCreatedAsync(client, mode); locations.Add(mode.Headers.Location!);
            using var point = await client.PostAsJsonAsync("/api/v1/route-points", new { type = "transport_stop", name = "Extra", lat = 0, lon = 0 });
            await HttpTestSupport.AssertCreatedAsync(client, point); locations.Add(point.Headers.Location!);
            using var route = await client.PostAsJsonAsync("/api/v1/transport-routes", TransportRequests.Route());
            await HttpTestSupport.AssertCreatedAsync(client, route); locations.Add(route.Headers.Location!);
        }
        using var second = new ApiFactory();
        using var restarted = second.CreateClient();
        foreach (var location in locations)
        {
            using var missing = await restarted.GetAsync(location);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        Assert.Equal(6, await HttpTestSupport.CountAsync(restarted, "/api/v1/transport-modes"));
        Assert.Equal(3, await HttpTestSupport.CountAsync(restarted, "/api/v1/route-points"));
        Assert.Equal(1, await HttpTestSupport.CountAsync(restarted, "/api/v1/transport-routes"));
    }
}

/**
 * Creates an isolated HTTP host and store for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /**
     * Configures an isolated test host.
     * @param builder The isolated test host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

/**
 * Supplies JSON HTTP helpers and assertions without external dependencies.
 */
internal static class HttpTestSupport
{
    /**
     * Builds JSON request content.
     * @param json Raw JSON to send.
     * @return UTF-8 JSON request content.
     */
    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    /**
     * Reads the current collection size.
     * @param client Test HTTP client.
     * @param path Collection path.
     * @return Number of current records.
     */
    public static async Task<int> CountAsync(HttpClient client, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(path)).GetArrayLength();

    /**
     * Checks the public validation response.
     * @param response Rejected request.
     * @return Completion after checking status and field errors.
     */
    public static async Task AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
    }

    /**
     * Checks creation and retrieves the saved representation.
     * @param client Test HTTP client.
     * @param response Successful creation.
     * @return The saved representation, verified at its Location.
     */
    public static async Task<JsonElement> AssertCreatedAsync(HttpClient client, HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var details = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.Equal(saved.GetRawText(), (await details.Content.ReadFromJsonAsync<JsonElement>()).GetRawText());
        return saved;
    }
}

/**
 * Supplies manually entered schedules and stable seed references.
 */
internal static class TransportRequests
{
    public const string Bus = "00000000-0000-0000-0000-000000000101";
    public const string Start = "00000000-0000-0000-0000-000000000201";
    public const string Destination = "00000000-0000-0000-0000-000000000202";
    public const string Stop = "00000000-0000-0000-0000-000000000203";

    /**
     * Builds a valid two-stop schedule with explicit UTC offsets.
     * @return A fresh schedule with two stops and explicit offsets.
     */
    public static JsonObject Route() => new()
    {
        ["name"] = " Scheduled Bus ", ["transportModeId"] = Bus,
        ["startPointId"] = Start, ["destinationPointId"] = Destination,
        ["departureAt"] = "2026-10-10T08:00:00-04:00", ["arrivalAt"] = "2026-10-10T16:00:00-04:00",
        ["boardingMinutes"] = 30,
        ["stops"] = new JsonArray(
            new JsonObject { ["pointId"] = Stop, ["arrivalAt"] = "2026-10-10T09:00:00-04:00", ["departureAt"] = "2026-10-10T10:30:00-04:00", ["boardingMinutes"] = 20, ["canLeaveVehicle"] = true },
            new JsonObject { ["pointId"] = Stop, ["arrivalAt"] = "2026-10-10T12:00:00-04:00", ["departureAt"] = "2026-10-10T13:20:00-04:00", ["boardingMinutes"] = 0, ["canLeaveVehicle"] = false })
    };
}
