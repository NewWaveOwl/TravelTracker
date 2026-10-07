using System.Text.Json.Nodes;
namespace TravelTracker.Attractions.Api.Tests;
/**
 * Builds a minimal valid create request for behavior tests.
 */
public static class AttractionRequests
{
    /**
     * Creates a resource and returns its details Location.
     * @return Fresh writable values; each test can change fields independently.
     */
    public static JsonObject Create() => new()
    {
        ["name"] = " New Museum ", ["lat"] = 45m, ["lon"] = -73m,
        ["estimatedVisitMinutes"] = 60, ["description"] = "A new place.", ["rating"] = 4m
    };
}
