using System.Net;
using System.Net.Http.Json;
namespace TravelTracker.Transport.Api.Tests;
/**
 * Verifies catalogue and schedule stores reset together in a fresh host.
 */
public sealed class TransportLifetimeHttpTests
{
    [Fact]
    public async Task FreshHost_DiscardsCataloguePointAndRouteWrites()
    {
        var locations = new List<Uri>();
        using (var first = new ApiFactory())
        using (var client = first.CreateClient())
        {
            using var mode = await client.PostAsJsonAsync("/api/v1/transport-modes", new { code = "bus", name = "Extra" });
            await HttpTestSupport.AssertCreatedAsync(client, mode); locations.Add(mode.Headers.Location!);
            using var point = await client.PostAsJsonAsync("/api/v1/route-points", new { type = "transport_stop", name = "Extra", lat = 0, lon = 0 });
            await HttpTestSupport.AssertCreatedAsync(client, point); locations.Add(point.Headers.Location!);
            using var route = await client.PostAsJsonAsync("/api/v1/transport-routes", TransportRequests.Route());
            await HttpTestSupport.AssertCreatedAsync(client, route); locations.Add(route.Headers.Location!);
        }
        using var second = new ApiFactory();
        using var restarted = second.CreateClient();
        foreach (var location in locations)
        {
            using var missing = await restarted.GetAsync(location);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        Assert.Equal(6, await HttpTestSupport.CountAsync(restarted, "/api/v1/transport-modes"));
        Assert.Equal(3, await HttpTestSupport.CountAsync(restarted, "/api/v1/route-points"));
        Assert.Equal(1, await HttpTestSupport.CountAsync(restarted, "/api/v1/transport-routes"));
    }
}
