using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Verifies the core Attractions milestone workflows with 15 xUnit cases.
 */
public sealed class AttractionsTests
{
    private const string Path = "/api/v1/attractions/11111111-1111-1111-1111-111111111111";

    /**
     * Checks the independent Attractions health endpoint.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Health_ReturnsServiceIdentity()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var value = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", value.GetProperty("status").GetString());
        Assert.Equal("Attractions", value.GetProperty("service").GetString());
    }

    /**
     * Checks ordered seed data and the eight public attraction fields.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task List_ReturnsStableSeedOrderAndWireFields()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var records = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions");
        Assert.Equal(new[] { "Montreal Museum", "Old Port", "Science Centre" },
            records.EnumerateArray().Select(value => value.GetProperty("name").GetString()));
        var first = records[0];
        Assert.Equal(new[] { "attractionId", "name", "description", "rating", "address", "lat", "lon", "estimatedVisitMinutes" },
            first.EnumerateObject().Select(field => field.Name));
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), first.GetProperty("attractionId").GetGuid());
        Assert.Equal(45.4987m, first.GetProperty("lat").GetDecimal());
        Assert.True(first.TryGetProperty("description", out _));
        Assert.Equal(JsonValueKind.Null, records[2].GetProperty("rating").ValueKind);
    }

    /**
     * Rejects an unknown or malformed attraction identifier.
     * @param id Unknown or malformed identifier.
     * @param expected Expected HTTP status.
     * @return Completion after the behavior is verified.
     */
    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000999", HttpStatusCode.NotFound)]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    public async Task Get_UsesDocumentedStatuses(string id, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions/" + id);
        Assert.Equal(expected, response.StatusCode);
    }

    /**
     * Creates a trimmed attraction with the maximum rating and reads its Location.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Create_ValidAttractionReturnsLocationAndSavedFields()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = AttractionRequests.Create();
        request["rating"] = 5;
        using var response = await client.PostAsJsonAsync("/api/v1/attractions", request);
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("attractionId").GetGuid());
        Assert.Equal("New Museum", saved.GetProperty("name").GetString());
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
        Assert.Equal(5m, saved.GetProperty("rating").GetDecimal());
        Assert.Equal(4, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    /**
     * Rejects a blank name and ratings just outside the permitted range without writing.
     * @param field Field to invalidate.
     * @param json Invalid JSON value for that field.
     * @return Completion after the behavior is verified.
     */
    [Theory]
    [InlineData("name", """ "   " """)]
    [InlineData("rating", "-0.1")]
    [InlineData("rating", "5.1")]
    public async Task Create_InvalidFieldDoesNotWrite(string field, string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = AttractionRequests.Create();
        request[field] = JsonNode.Parse(json);
        using var response = await client.PostAsJsonAsync("/api/v1/attractions", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    /**
     * Searches with spaces and mixed case in one representative query.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Search_TrimsQueryAndIgnoresCase()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions?name=%20%20mUsEuM%20%20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var results = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Montreal Museum", Assert.Single(results.EnumerateArray()).GetProperty("name").GetString());
    }

    /**
     * Returns all records for an empty query and an empty list when nothing matches.
     * @param name Empty or unmatched query.
     * @param expected Expected number of results.
     * @return Completion after the behavior is verified.
     */
    [Theory]
    [InlineData("", 3)]
    [InlineData("no-such-name", 0)]
    public async Task Search_EmptyAndUnmatchedQueriesReturnExpectedLists(string name, int expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(expected, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=" + Uri.EscapeDataString(name)));
    }

    /**
     * Updates selected fields, preserves omitted fields, clears optionals and refreshes search.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Patch_PreservesOmittedFieldsAndClearsOnlyOptionals()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetFromJsonAsync<JsonElement>(Path);
        using var response = await client.PatchAsync(Path, HttpTestSupport.Json(
            """{"name":" Renamed Gallery ","description":null,"rating":0,"lat":0,"lon":0,"estimatedVisitMinutes":0}"""));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(before.GetProperty("attractionId").GetGuid(), saved.GetProperty("attractionId").GetGuid());
        Assert.Equal(before.GetProperty("address").GetString(), saved.GetProperty("address").GetString());
        Assert.Equal("Renamed Gallery", saved.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("description").ValueKind);
        Assert.Equal(0, saved.GetProperty("rating").GetDecimal());
        Assert.Equal(0, saved.GetProperty("lat").GetDecimal());
        using var clear = await client.PatchAsync(Path, HttpTestSupport.Json("""{"rating":null}"""));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await clear.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rating").ValueKind);
        Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=renamed"));
        Assert.Equal(0, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=Montreal%20Museum"));
    }

    /**
     * Rejects a mixed valid and invalid update without changing any saved field.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Patch_InvalidCandidateLeavesEntireRecordUnchanged()
    {
        const string json = """{"name":"Valid new name","rating":6}""";
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync(Path);
        using var response = await client.PatchAsync(Path, HttpTestSupport.Json(json));
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(before, await client.GetStringAsync(Path));
    }

    /**
     * Returns 404 for an update to an absent attraction without inserting anything.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task Patch_UnknownIdReturns404WithoutWriting()
    {
        const string id = "00000000-0000-0000-0000-000000000999";
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/" + id, HttpTestSupport.Json("""{"name":"Valid"}"""));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    /**
     * Discards writes and restores seeds in a new in-memory host.
     * @return Completion after the behavior is verified.
     */
    [Fact]
    public async Task FreshHost_RestoresSeedsAndDiscardsPriorWrites()
    {
        Uri location;
        using (var first = new ApiFactory())
        using (var client = first.CreateClient())
        {
            using var create = await client.PostAsJsonAsync("/api/v1/attractions", AttractionRequests.Create());
            await HttpTestSupport.AssertCreatedAsync(client, create);
            location = create.Headers.Location!;
            using var patch = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111",
                HttpTestSupport.Json("""{"name":"Changed Seed"}"""));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            Assert.Equal(4, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
        }
        using var second = new ApiFactory();
        using var restarted = second.CreateClient();
        using var missing = await restarted.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(3, await HttpTestSupport.CountAsync(restarted, "/api/v1/attractions"));
        Assert.Equal(1, await HttpTestSupport.CountAsync(restarted, "/api/v1/attractions?name=Montreal%20Museum"));
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
 * Builds a minimal valid create request for behavior tests.
 */
internal static class AttractionRequests
{
    /**
     * Builds a fresh valid attraction request without sending it.
     * @return Fresh writable values; each test can change fields independently.
     */
    public static JsonObject Create() => new()
    {
        ["name"] = " New Museum ", ["lat"] = 45m, ["lon"] = -73m,
        ["estimatedVisitMinutes"] = 60, ["description"] = "A new place.", ["rating"] = 4m
    };
}
