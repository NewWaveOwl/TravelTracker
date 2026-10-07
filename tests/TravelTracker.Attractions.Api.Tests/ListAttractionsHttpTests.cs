using System.Net.Http.Json;
using System.Text.Json;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Verifies ordered seed data and the complete public representation.
 */
public sealed class ListAttractionsHttpTests
{
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
}
