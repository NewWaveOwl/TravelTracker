using System.Text.Json;
using System.Text.Json.Nodes;

namespace TravelTracker.Trips.Api.Tests.Support;

/** reads problem bodies in a form two responses can be compared on. */
internal static class ProblemBodies
{
    /**
     * reads a response body without its per-request trace id, so two refusals can be compared for equality.
     * @param response the response to read.
     * @return the body text with the traceId member removed.
     */
    internal static async Task<string> WithoutTraceIdAsync(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }
}
