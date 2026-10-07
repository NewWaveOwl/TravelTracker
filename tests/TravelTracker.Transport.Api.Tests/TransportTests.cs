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
 * Verifies the core Transport milestone workflows with 15 xUnit cases.
 */
public sealed class TransportTests
{
    /**
     * Checks the independent Transport health endpoint.
     * @return Completion after the behavior is verified.
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
     * Lists the six seeded modes and creates a named bus entry without a schedule.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_TransportEntryDoesNotCreateASchedule()
    {
        const string code = "bus";
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var modes = await client.GetFromJsonAsync<JsonElement>("/api/v1/transport-modes");
        Assert.Equal(new[] { "bike", "bus", "car", "pedestrian", "plane", "train" },
            modes.EnumerateArray().Select(mode => mode.GetProperty("code").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        var routesBefore = await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes");
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code, name = " New Transport ", description = "A manual entry." });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal("New Transport", saved.GetProperty("name").GetString());
        Assert.Equal(code, saved.GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, saved.GetProperty("transportModeId").GetGuid());
        Assert.Equal(7, await HttpTestSupport.CountAsync(client, "/api/v1/transport-modes"));
        Assert.Equal(routesBefore, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    /**
     * Rejects an unsupported transport mode without inserting an entry.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_UnsupportedModeDoesNotWrite()
    {
        const string json = """{"code":"rocket","name":"Test"}""";
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/transport-modes", HttpTestSupport.Json(json));
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(6, await HttpTestSupport.CountAsync(client, "/api/v1/transport-modes"));
    }

    /**
     * Creates the three point roles and uses them in one scheduled journey.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_RoutePointsFormScheduledJourney()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var source = Guid.Parse("00000000-0000-0000-0000-000000000999");
        var points = new Dictionary<string, string>();
        foreach (var type in new[] { "accommodation", "attraction", "transport_stop" })
        {
            using var response = await client.PostAsJsonAsync("/api/v1/route-points",
                new { type, name = $" New {type} ", lat = -90m, lon = 180m, sourceId = source });
            var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
            Assert.Equal(type, saved.GetProperty("type").GetString());
            Assert.Equal($"New {type}", saved.GetProperty("name").GetString());
            Assert.Equal(source, saved.GetProperty("sourceId").GetGuid());
            points[type] = saved.GetProperty("routePointId").GetGuid().ToString();
        }
        Assert.Equal(6, await HttpTestSupport.CountAsync(client, "/api/v1/route-points"));
        var request = TransportRequests.Route();
        request["startPointId"] = points["accommodation"];
        request["destinationPointId"] = points["attraction"];
        var stop = request["stops"]![0]!.DeepClone();
        stop["pointId"] = points["transport_stop"];
        request["stops"] = new JsonArray(stop);
        using var routeResponse = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        var journey = await HttpTestSupport.AssertCreatedAsync(client, routeResponse);
        Assert.Equal(points["accommodation"], journey.GetProperty("start").GetProperty("routePointId").GetGuid().ToString());
        Assert.Equal(points["attraction"], journey.GetProperty("destination").GetProperty("routePointId").GetGuid().ToString());
        Assert.Equal(points["transport_stop"], journey.GetProperty("stops")[0].GetProperty("point").GetProperty("routePointId").GetGuid().ToString());
    }

    /**
     * Rejects a point outside the latitude range without inserting it.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_InvalidPointCoordinatesDoNotWrite()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/route-points",
            new { type = "attraction", name = "Invalid point", lat = 91, lon = 0 });
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/route-points"));
    }

    /**
     * Checks absent and malformed identifiers for each transport resource.
     * @param resource Resource collection to query.
     * @return Completion after the behavior is verified.
     */
    [Theory]
    [InlineData("transport-modes")]
    [InlineData("route-points")]
    [InlineData("transport-routes")]
    public async Task Get_AbsentAndMalformedResourcesUseDocumentedStatuses(string resource)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var missing = await client.GetAsync($"/api/v1/{resource}/00000000-0000-0000-0000-000000000999");
        using var invalid = await client.GetAsync($"/api/v1/{resource}/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await HttpTestSupport.AssertValidationAsync(invalid);
    }

    /**
     * Checks UTC times, origin preparation, multiple stops and permitted free windows.
     * @return Completion after the behavior is verified.
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

    /**
     * Checks a direct plane journey crossing midnight with origin preparation.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_DirectOvernightPlaneRouteHasCorrectDuration()
    {
        const string id = "00000000-0000-0000-0000-000000000105";
        const string code = "plane";
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
     * Rejects a timestamp without an offset and a non-increasing journey without writing.
     * @param field Schedule field to invalidate.
     * @param json Invalid JSON value for that field.
     * @return Completion after the behavior is verified.
     */
    [Theory]
    [InlineData("departureAt", "\"2026-10-10T08:00:00\"")]
    [InlineData("arrivalAt", "\"2026-10-10T08:00:00-04:00\"")]
    public async Task Create_InvalidRouteFieldDoesNotWrite(string field, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route(); request[field] = JsonNode.Parse(json);
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    /**
     * Rejects boarding time longer than the stop window without inserting a route.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_StopBoardingCannotExceedDowntime()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route();
        request["stops"]![0]!["boardingMinutes"] = 91;
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    /**
     * Rejects an absent destination after reference resolution without partial insertion.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_UnknownDestinationReturns404WithoutWriting()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = TransportRequests.Route();
        request["destinationPointId"] = "00000000-0000-0000-0000-000000000999";
        using var response = await client.PostAsJsonAsync("/api/v1/transport-routes", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/transport-routes"));
    }

    /**
     * Resets entries, points and scheduled routes in a new in-memory host.
     * @return Completion after the behavior is verified.
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
