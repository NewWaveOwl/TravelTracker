using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TravelTracker.Settings.Api.Access;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * implements the documented Users identity and standing lookups inside an isolated API test.
 * the subject decides standing: "banned" and "deactivated" map to those states, anything else is an active traveller.
 */
internal sealed class UsersBackchannelHandler : HttpMessageHandler
{
    private readonly object gate = new();
    private readonly Dictionary<string, Guid> idsBySubject = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> subjectsById = [];

    internal int Calls { get; private set; }
    internal bool Unavailable { get; set; }

    /**
     * returns the requested account identity or standing for an authorized service request.
     * state is guarded because concurrent requests reach this handler from different threads.
     * @param request the backchannel HTTP request.
     * @param cancellationToken cancels the simulated request.
     * @return a minimal Users response, or an error response.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Calls++;
        }
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
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = ResolveIdentity(subject) }) };
        }

        var pathParts = request.RequestUri?.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (request.Method == HttpMethod.Get && pathParts is { Length: 4 } &&
            pathParts[0] == "internal" && pathParts[1] == "users" && pathParts[3] == "access" &&
            Guid.TryParse(pathParts[2], out var userId) && TryGetSubject(userId, out var resolvedSubject))
        {
            var status = resolvedSubject switch
            {
                "banned" => "banned",
                "deactivated" => "deactivated",
                _ => "active"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { userId, status, role = "traveller" })
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /**
     * returns the stable identifier for a subject, creating it on first sight.
     * @param subject the validated token subject.
     * @return the user identifier used for every later lookup of this subject.
     */
    private Guid ResolveIdentity(string subject)
    {
        lock (gate)
        {
            if (idsBySubject.TryGetValue(subject, out var id)) return id;
            id = Guid.CreateVersion7();
            idsBySubject[subject] = id;
            subjectsById[id] = subject;
            return id;
        }
    }

    /**
     * finds the subject behind a user identifier.
     * @param userId the identifier from the access route.
     * @param subject the subject when the identifier is known.
     * @return true when the identifier was created by this stub.
     */
    private bool TryGetSubject(Guid userId, out string? subject)
    {
        lock (gate)
        {
            return subjectsById.TryGetValue(userId, out subject);
        }
    }
}
