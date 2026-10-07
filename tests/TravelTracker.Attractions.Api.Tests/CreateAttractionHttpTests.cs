using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Proves creation, public Location and rejected writes through HTTP.
 */
public sealed class CreateAttractionHttpTests
{
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
}
