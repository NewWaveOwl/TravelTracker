using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Api.Tests;

/**
 * Verifies the shared API health endpoint through HTTP.
 */
public sealed class HealthTests
{
    /**
     * Checks the shared API health response.
     * @return Completion after checking service identity.
     */
    [Fact]
    public async Task Health_ReturnsServiceIdentity()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var value = await client.GetFromJsonAsync<JsonElement>("/health");
        Assert.Equal("ok", value.GetProperty("status").GetString());
        Assert.Equal("TravelTracker", value.GetProperty("service").GetString());
    }
}
