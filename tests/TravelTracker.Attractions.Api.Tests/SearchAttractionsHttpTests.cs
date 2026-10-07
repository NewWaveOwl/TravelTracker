using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Attractions.Api.Data;
using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Verifies literal name matching, decoding and stable tie ordering.
 */
public sealed class SearchAttractionsHttpTests
{
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
}
