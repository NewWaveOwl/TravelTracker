using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Transport.Api.Tests;

/**
 * Verifies simple transport entries and route points in thirteen cases.
 */
public sealed class TransportTests
{
    /**
     * Checks the Transport health response.
     * @return Completion after checking service identity.
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
     * Reads a seeded transport entry.
     * @return Completion after checking the seed.
     */
    [Fact]
    public async Task GetModeById_KnownEntryReturnsSeed()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var saved = await client.GetFromJsonAsync<JsonElement>("/api/v1/transport-modes/00000000-0000-0000-0000-000000000101");
        Assert.Equal("bus", saved.GetProperty("code").GetString());
        Assert.Equal("Example Bus", saved.GetProperty("name").GetString());
    }

    /**
     * Returns 404 for an absent transport entry.
     * @return Completion after checking the lookup response.
     */
    [Fact]
    public async Task GetModeById_UnknownEntryReturns404()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/transport-modes/00000000-0000-0000-0000-000000000999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /**
     * Creates a named plane entry and reads its Location.
     * @return Completion after checking creation and lookup.
     */
    [Fact]
    public async Task CreateMode_ValidEntryReturns201AndSavedLocation()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes",
            new { code = "plane", name = " Example Airline ", description = "Manual transport entry." });
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("transportModeId").GetGuid());
        Assert.Equal("plane", saved.GetProperty("code").GetString());
        Assert.Equal("Example Airline", saved.GetProperty("name").GetString());
        Assert.Equal("Manual transport entry.", saved.GetProperty("description").GetString());
    }

    /**
     * Rejects an unsupported transport code.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreateMode_UnsupportedCodeReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "rocket", name = "Example" });
        await HttpTestSupport.AssertValidationAsync(response);
    }

    /**
     * Rejects a whitespace-only transport name.
     * @return Completion after checking validation.
     */
    [Fact]
    public async Task CreateMode_BlankNameReturns400()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bus", name = "   " });
        await HttpTestSupport.AssertValidationAsync(response);
    }

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

/**
 * Creates an isolated in-memory HTTP host for each test.
 */
internal sealed class ApiFactory : WebApplicationFactory<Program>
{
    /**
     * Configures the test host.
     * @param builder The isolated host builder.
     */
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

/**
 * Checks shared creation and validation HTTP contracts.
 */
internal static class HttpTestSupport
{
    /**
     * Checks creation and reads the saved record at its Location.
     * @param client The isolated test client.
     * @param response The creation response.
     * @return The saved JSON record.
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

    /**
     * Checks the standard ASP.NET Core validation response.
     * @param response The rejected request's response.
     * @return Completion after checking status and field errors.
     */
    public static async Task AssertValidationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").EnumerateObject().Any());
    }
}
