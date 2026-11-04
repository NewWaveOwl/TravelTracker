using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Api.Tests;

public sealed class AttractionQueryTests
{
    [Theory]
    [InlineData("name=museum&name=port")]
    [InlineData("name=museum&name=museum")]
    [InlineData("name=&name=")]
    [InlineData("name=%20&name=")]
    [InlineData("name=museum&%6Eame=port")]
    [InlineData("NAME=museum&name=port")]
    public async Task RepeatedName_ReturnsValidationProblemAndPreservesCatalogue(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var before = await client.GetStringAsync("/api/v1/attractions");
        using var response = await client.GetAsync("/api/v1/attractions?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Contains("at most once", problem.GetProperty("errors").GetProperty("name")[0].GetString());
        Assert.Equal(before, await client.GetStringAsync("/api/v1/attractions"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("?name=")]
    [InlineData("?name=%20%20")]
    public async Task EmptyName_ReturnsCompleteCatalogue(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(await client.GetStringAsync("/api/v1/attractions"),
            await client.GetStringAsync("/api/v1/attractions" + query));
    }

    [Theory]
    [InlineData("%20PORT%20", "Old Port")]
    [InlineData("%6Duseum", "Montreal Museum")]
    public async Task SingleName_PreservesTrimmedCaseInsensitiveSearch(string query, string expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var results = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=" + query);
        Assert.NotNull(results);
        Assert.Equal(expected, Assert.Single(results).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("%25")]
    [InlineData("%5F")]
    [InlineData("museum%2Cport")]
    public async Task SingleName_MatchesLiterally(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var results = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/attractions?name=" + query);
        Assert.NotNull(results);
        Assert.Empty(results);
    }
}
