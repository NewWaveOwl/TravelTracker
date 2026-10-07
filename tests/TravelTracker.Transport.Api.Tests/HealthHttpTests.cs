using System.Net.Http.Json;
using System.Text.Json;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies independent service health.
 */
public sealed class HealthHttpTests
{
    [Fact]
    public async Task Health_ReturnsServiceIdentity()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var value = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", value.GetProperty("status").GetString());
        Assert.Equal("Transport", value.GetProperty("service").GetString());
    }
}
