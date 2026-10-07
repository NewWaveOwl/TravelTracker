using System.Net;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies catalogue, point and route detail status contracts.
 */
public sealed class TransportDetailsHttpTests
{
    [Theory]
    [InlineData("transport-modes", "00000000-0000-0000-0000-000000000101")]
    [InlineData("route-points", "00000000-0000-0000-0000-000000000201")]
    [InlineData("transport-routes", "00000000-0000-0000-0000-000000000301")]
    public async Task Get_HandlesKnownUnknownAndMalformedIds(string resource, string known)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var found = await client.GetAsync($"/api/v1/{resource}/{known}");
        using var missing = await client.GetAsync($"/api/v1/{resource}/00000000-0000-0000-0000-000000000999");
        using var invalid = await client.GetAsync($"/api/v1/{resource}/not-a-guid");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await HttpTestSupport.AssertValidationAsync(invalid);
    }
}
