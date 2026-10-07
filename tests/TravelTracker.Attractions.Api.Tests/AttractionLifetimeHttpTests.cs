using System.Net;
using System.Net.Http.Json;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Verifies process-store lifetime through isolated hosts.
 */
public sealed class AttractionLifetimeHttpTests
{
    [Fact]
    public async Task FreshHost_RestoresSeedsAndDiscardsPriorWrites()
    {
        Uri location;
        using (var first = new ApiFactory())
        using (var client = first.CreateClient())
        {
            using var create = await client.PostAsJsonAsync("/api/v1/attractions", AttractionRequests.Create());
            await HttpTestSupport.AssertCreatedAsync(client, create);
            location = create.Headers.Location!;
            using var patch = await client.PatchAsync("/api/v1/attractions/11111111-1111-1111-1111-111111111111",
                HttpTestSupport.Json("""{"name":"Changed Seed"}"""));
            Assert.Equal(HttpStatusCode.OK, patch.StatusCode);
            Assert.Equal(4, await HttpTestSupport.CountAsync(client, "/api/v1/attractions"));
        }
        using var second = new ApiFactory();
        using var restarted = second.CreateClient();
        using var missing = await restarted.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(3, await HttpTestSupport.CountAsync(restarted, "/api/v1/attractions"));
        Assert.Equal(1, await HttpTestSupport.CountAsync(restarted, "/api/v1/attractions?name=Montreal%20Museum"));
    }
}
