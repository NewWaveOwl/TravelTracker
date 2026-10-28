using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Attractions.Api.Access;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Implements only the documented Users consumer boundary inside an isolated HTTP test.
 * @param unavailable Whether the dependency fails.
 */
internal sealed class UsersBackchannelHandler(bool unavailable) : HttpMessageHandler
{
    internal int Calls { get; private set; }
    internal string? ForcedStatus { get; set; }
    internal bool InvalidCharset { get; init; }

    /**
     * Returns live standing determined by the validated test subject.
     * @param request The service-authenticated lookup request.
     * @param cancellationToken Cancels lookup.
     * @return A current Users response, or simulated unavailability.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (unavailable) return new(HttpStatusCode.ServiceUnavailable);
        if (request.Method != HttpMethod.Post || request.Headers.Authorization?.Parameter != "test-service-token")
            return new(HttpStatusCode.Unauthorized);
        var identity = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var subject = identity.GetProperty("subject").GetString();
        var status = ForcedStatus ?? (subject is "banned" or "deactivated" ? subject : "active");
        var role = subject is "test-traveller" or "admin" ? "administrator" : "traveller";
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new UserAccessRecord(status!, role)) };
        if (InvalidCharset) response.Content.Headers.ContentType!.CharSet = "unregistered-test-charset";
        return response;
    }
}
