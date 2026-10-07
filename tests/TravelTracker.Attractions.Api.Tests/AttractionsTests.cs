using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelTracker.Attractions.Api.Data;
using TravelTracker.Attractions.Api.Models;
using TravelTracker.Attractions.Api.Validation;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Verifies the Attractions service with LAB_07-style xUnit tests in one file.
 */
public sealed class AttractionsTests
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
        Assert.Equal("Attractions", value.GetProperty("service").GetString());
    }

    /**
     * Verifies ordered seed data and the complete public representation.
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
     * Verifies detail identity and missing/malformed identifier semantics.
     */
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", HttpStatusCode.OK)]
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
     * Proves creation, public Location and rejected writes through HTTP.
     */
    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(null)]
    public async Task Create_StoresTrimmedNameAndInclusiveNullableRating(int? rating)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = AttractionRequests.Create();
        request["rating"] = rating;
        using var response = await client.PostAsJsonAsync("/api/v1/attractions", request);
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.NotEqual(Guid.Empty, saved.GetProperty("attractionId").GetGuid());
        Assert.Equal("New Museum", saved.GetProperty("name").GetString());
        Assert.Equal(string.Empty, saved.GetProperty("address").GetString());
        Assert.Equal(rating is null ? JsonValueKind.Null : JsonValueKind.Number, saved.GetProperty("rating").ValueKind);
        Assert.Equal(4, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    [Fact]
    public async Task Create_OmittedOptionalsAndZeroRequiredNumbersAreAccepted()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/attractions", HttpTestSupport.Json(
            """{"name":"Zero point","lat":0,"lon":0,"estimatedVisitMinutes":0}"""));
        var saved = await HttpTestSupport.AssertCreatedAsync(client, response);
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("rating").ValueKind);
    }

    [Theory]
    [InlineData("name", "null")]
    [InlineData("name", """ "   " """)]
    [InlineData("rating", "-0.1")]
    [InlineData("rating", "5.1")]
    [InlineData("lat", "91")]
    [InlineData("lon", "-181")]
    [InlineData("estimatedVisitMinutes", "-1")]
    [InlineData("estimatedVisitMinutes", "1.5")]
    [InlineData("address", "null")]
    [InlineData("rating", "\"high\"")]
    [InlineData("attractionId", "\"00000000-0000-0000-0000-000000000001\"")]
    [InlineData("unexpected", "true")]
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

    [Theory]
    [InlineData("name")]
    [InlineData("lat")]
    [InlineData("lon")]
    [InlineData("estimatedVisitMinutes")]
    public async Task Create_MissingRequiredFieldDoesNotWrite(string field)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var request = AttractionRequests.Create();
        request.Remove(field);
        using var response = await client.PostAsJsonAsync("/api/v1/attractions", request);
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    [Fact]
    public async Task Create_MalformedJsonDoesNotWrite()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/v1/attractions", HttpTestSupport.Json("{"));
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    /**
     * Verifies literal name matching, decoding and stable tie ordering.
     */
    [Theory]
    [InlineData("mUsEuM", 1)]
    [InlineData("  museum  ", 1)]
    [InlineData("", 3)]
    [InlineData("   ", 3)]
    [InlineData("no-such-name", 0)]
    public async Task Search_UsesTrimmedCaseInsensitiveSubstring(string name, int expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(expected, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=" + Uri.EscapeDataString(name)));
    }

    [Fact]
    public async Task Search_LiteralSymbolsAndSingleDecodingDoNotExpand()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        foreach (var name in new[] { "Literal %_*[museum]", "Literal %20" })
        {
            var request = AttractionRequests.Create(); request["name"] = name;
            using var created = await client.PostAsJsonAsync("/api/v1/attractions", request);
            await HttpTestSupport.AssertCreatedAsync(client, created);
        }
        foreach (var literal in new[] { "_", "*", "[museum]", "%20" })
            Assert.Equal(1, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=" + Uri.EscapeDataString(literal)));
        Assert.Equal(5, await HttpTestSupport.CountAsync(client, "/api/v1/attractions?name=%20"));
        using var repeated = await client.GetAsync("/api/v1/attractions?name=museum&name=port");
        await HttpTestSupport.AssertValidationAsync(repeated);
        Assert.Equal(5, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    [Fact]
    public async Task Search_CaseEqualNamesUseGuidTieBreaker()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<InMemoryAttractionStore>();
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        store.Add(new(high, "same", null, null, "", 0, 0, 0));
        store.Add(new(low, "SAME", null, null, "", 0, 0, 0));
        var records = await client.GetFromJsonAsync<JsonElement>("/api/v1/attractions?name=same");
        Assert.Equal(new[] { low, high }, records.EnumerateArray().Select(value => value.GetProperty("attractionId").GetGuid()));
    }

    /**
     * Proves partial updates, atomic rejection and visibility across requests.
     */
    private const string Path = "/api/v1/attractions/11111111-1111-1111-1111-111111111111";
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

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"lat\":null}")]
    [InlineData("{\"name\":\"Valid new name\",\"rating\":6}")]
    [InlineData("{\"rating\":-1}")]
    [InlineData("{\"lon\":181}")]
    [InlineData("{\"estimatedVisitMinutes\":-1}")]
    [InlineData("{\"estimatedVisitMinutes\":1.5}")]
    [InlineData("{\"description\":12}")]
    [InlineData("{\"attractionId\":\"00000000-0000-0000-0000-000000000001\"}")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("{\"name\":\"First\",\"name\":\"Second\"}")]
    public async Task Patch_InvalidCandidateLeavesEntireRecordUnchanged(string json)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync(Path);
        using var response = await client.PatchAsync(Path, HttpTestSupport.Json(json));
        await HttpTestSupport.AssertValidationAsync(response);
        Assert.Equal(before, await client.GetStringAsync(Path));
    }

    [Theory]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    [InlineData("00000000-0000-0000-0000-000000000999", HttpStatusCode.NotFound)]
    public async Task Patch_AbsentAndMalformedIdsUseDocumentedStatuses(string id, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PatchAsync("/api/v1/attractions/" + id, HttpTestSupport.Json("""{"name":"Valid"}"""));
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(3, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
    }

    [Fact]
    public async Task Patch_ConcurrentDifferentFieldsRetainBothChanges()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var responses = await Task.WhenAll(
            client.PatchAsync(Path, HttpTestSupport.Json("""{"name":"Concurrent Gallery"}""")),
            client.PatchAsync(Path, HttpTestSupport.Json("""{"rating":5}""")));
        foreach (var response in responses) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); response.Dispose(); }
        var saved = await client.GetFromJsonAsync<JsonElement>(Path);
        Assert.Equal("Concurrent Gallery", saved.GetProperty("name").GetString());
        Assert.Equal(5, saved.GetProperty("rating").GetDecimal());
    }

    /**
     * Verifies the compound store boundary independently of HTTP binding.
     */
    [Fact]
    public void Update_ValidationExceptionLeavesOriginalSnapshotIntact()
    {
        var store = new InMemoryAttractionStore();
        var before = store.Snapshot()[0];
        Assert.Throws<InputValidationException>(() => store.Update(before.AttractionId, current =>
        {
            var candidate = current with { Name = "New", Rating = 6 };
            AttractionValidator.Validate(candidate);
            return candidate;
        }));
        Assert.Equal(before, store.Get(before.AttractionId));
    }

    [Fact]
    public void Update_DoesNotMutateEarlierReadSnapshots()
    {
        var store = new InMemoryAttractionStore();
        var before = store.Snapshot()[0];
        var saved = store.Update(before.AttractionId, current => current with { Name = "New" });
        Assert.NotEqual(before.Name, saved!.Name);
        Assert.Equal("New", store.Get(before.AttractionId)!.Name);
        Assert.Equal("Montreal Museum", before.Name);
    }

    /**
     * Verifies process-store lifetime through isolated hosts.
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
