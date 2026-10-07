using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Proves partial updates, atomic rejection and visibility across requests.
 */
public sealed class PatchAttractionHttpTests
{
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
}
