using System.Net.Http.Json;

namespace TravelTracker.Settings.Api.Tests.Support;

/** sends settings replacements the way a well-behaved client does, with an If-Match header. */
internal static class SettingsClientExtensions
{
    /**
     * replaces the caller's settings using the ETag from a fresh read, the way a client that has just loaded the form would.
     * @param client the authenticated test client.
     * @param body the complete preference object to send.
     * @return the replace response.
     * @throws InvalidOperationException when the read does not return an ETag.
     */
    internal static async Task<HttpResponseMessage> PutSettingsAsync(this HttpClient client, object body)
    {
        using var current = await client.GetAsync("/api/v1/settings");
        var etag = current.Headers.ETag?.Tag ??
            throw new InvalidOperationException($"settings read returned {(int)current.StatusCode} without an ETag.");
        return await client.PutSettingsAsync(body, etag);
    }

    /**
     * replaces the caller's settings with an explicit If-Match value, including missing or malformed ones.
     * @param client the authenticated test client.
     * @param body the complete preference object to send.
     * @param ifMatch the raw If-Match value, or null to send no header.
     * @return the replace response.
     */
    internal static Task<HttpResponseMessage> PutSettingsAsync(this HttpClient client, object body, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/settings") { Content = JsonContent.Create(body) };
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return client.SendAsync(request);
    }
}
