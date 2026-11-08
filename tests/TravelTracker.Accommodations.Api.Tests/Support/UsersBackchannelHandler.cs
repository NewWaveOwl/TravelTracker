using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Accommodations.Api.Tests.Support;

/** Implements the documented Users identity and standing lookups inside an isolated API test. */
internal sealed class UsersBackchannelHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Guid> idsBySubject = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> subjectsById = new();

    internal int Calls { get; private set; }
    internal bool Unavailable { get; set; }

    /**
     * Returns the requested account identity or access state for an authorized service request.
     * @param request The backchannel HTTP request.
     * @param cancellationToken Cancels the simulated request.
     * @return A minimal Users response or an error response.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Unavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (request.Headers.Authorization?.Scheme != "Bearer" || request.Headers.Authorization.Parameter != ApiFactory.ServiceToken)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/internal/users/identity")
        {
            var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var issuer = payload.GetProperty("issuer").GetString();
            var subject = payload.GetProperty("subject").GetString();
            if (issuer != ApiFactory.Issuer || string.IsNullOrWhiteSpace(subject))
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            var id = idsBySubject.GetOrAdd(subject, _ => Guid.CreateVersion7());
            subjectsById.TryAdd(id, subject);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = id }) };
        }

        var pathParts = request.RequestUri?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (request.Method == HttpMethod.Get && pathParts is { Length: 4 } &&
            pathParts[0] == "internal" && pathParts[1] == "users" && pathParts[3] == "access" &&
            Guid.TryParse(pathParts[2], out var userId) && subjectsById.TryGetValue(userId, out var resolvedSubject))
        {
            var status = resolvedSubject switch
            {
                "banned" => "banned",
                "deactivated" => "deactivated",
                _ => "active"
            };
            var role = resolvedSubject == "admin" ? "administrator" : "traveller";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { userId, status, role })
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
