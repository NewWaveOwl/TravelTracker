using System.Net;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Verifies detail identity and missing/malformed identifier semantics.
 */
public sealed class GetAttractionHttpTests
{
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", HttpStatusCode.OK)]
    [InlineData("00000000-0000-0000-0000-000000000999", HttpStatusCode.NotFound)]
    [InlineData("not-a-guid", HttpStatusCode.BadRequest)]
    public async Task Get_UsesDocumentedStatuses(string id, HttpStatusCode expected)
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/v1/attractions/" + id);
        Assert.Equal(expected, response.StatusCode);
    }
}
