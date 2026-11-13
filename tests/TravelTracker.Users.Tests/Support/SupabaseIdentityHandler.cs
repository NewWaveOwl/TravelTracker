using System.Net;
using System.Text.Json.Nodes;

namespace TravelTracker.Users.Tests.Support;

/** Returns a controlled Supabase Auth user document without storing or logging real credentials. */
internal sealed class SupabaseIdentityHandler : HttpMessageHandler
{
    internal JsonObject User { get; } = new()
    {
        ["id"] = "10d5c7fe-8989-4a43-b68a-9c45e9a73bd8",
        ["email"] = "verified@example.test",
        ["email_confirmed_at"] = "2026-11-12T12:00:00Z",
        ["is_anonymous"] = false,
        ["user_metadata"] = new JsonObject { ["email"] = "attacker@example.test", ["email_verified"] = true }
    };
    internal int Calls { get; private set; }
    internal Uri? Destination { get; private set; }
    internal string? PublicKey { get; private set; }
    internal string? Bearer { get; private set; }

    /**
     * Captures only test-owned request values and returns the scenario's provider response.
     * @param request Identity verification request from the production client.
     * @param cancellationToken Cancels the fake response.
     * @return A provider-shaped successful response.
     */
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Destination = request.RequestUri;
        PublicKey = request.Headers.TryGetValues("apikey", out var keys) ? keys.Single() : null;
        Bearer = request.Headers.Authorization?.ToString();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(User.ToJsonString()) });
    }
}
