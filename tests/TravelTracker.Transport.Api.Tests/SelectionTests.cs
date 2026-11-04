using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TravelTracker.Transport.Api.Tests;

/** Proves selection uses saved values and the independent host's operation policies. */
public sealed class SelectionTests
{
    [Theory]
    [InlineData("transport-modes", "code=bike", "code", "bike")]
    [InlineData("route-points", "type=transport_stop", "type", "transport_stop")]
    public async Task Selection_FiltersSavedSeedRecords(string resource, string query, string field, string value)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var records = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/{resource}?{query}");
        Assert.NotEmpty(records!);
        Assert.All(records!, record => Assert.Equal(value, record.GetProperty(field).GetString()));
        var absent = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/{resource}?name=no-such-record");
        Assert.Empty(absent!);
    }

    [Theory]
    [InlineData("transport-modes?code=boat")]
    [InlineData("route-points?type=airport")]
    [InlineData("transport-modes?code=bike&code=bus")]
    [InlineData("route-points?name=a&NAME=b")]
    public async Task Selection_InvalidFilterReturns400(string query)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/" + query)).StatusCode);
    }

    [Theory]
    [InlineData("transport-modes", null, 401)]
    [InlineData("transport-modes", "catalogue.write", 403)]
    [InlineData("transport-modes", "catalogue.read", 200)]
    [InlineData("route-points", null, 401)]
    [InlineData("route-points", "catalogue.write", 403)]
    [InlineData("route-points", "catalogue.read", 200)]
    public async Task Selection_RequiresReadScope(string resource, string? scope, int status)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = scope is null ? null : new AuthenticationHeaderValue("Bearer", factory.CreateToken(scope));
        Assert.Equal(status, (int)(await client.GetAsync("/api/v1/" + resource)).StatusCode);
    }

    [Fact]
    public async Task Selection_ReturnsNewRecordsWithPublicFieldsAndStableOrdering()
    {
        using var factory = new ApiFactory { SeedExamples = false };
        using var client = factory.CreateClient();
        foreach (var name in new[] { "Zoo", "alpha", "Alpha" })
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bike", name })).StatusCode);
        var records = (await client.GetFromJsonAsync<JsonElement[]>("/api/v1/transport-modes?name=ALP"))!;
        Assert.Equal(2, records.Length);
        Assert.Equal(records.Select(record => record.GetProperty("transportModeId").GetGuid()).Order(), records.Select(record => record.GetProperty("transportModeId").GetGuid()));
        Assert.All(records, record => Assert.Equal(new[] { "code", "description", "name", "transportModeId" }, record.EnumerateObject().Select(property => property.Name).Order()));
    }
}
