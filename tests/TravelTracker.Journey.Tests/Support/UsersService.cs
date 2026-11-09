using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Journey.Tests.Support;

/**
 * plays the Users service's internal identity and standing lookups that the other hosts call.
 * the subject decides standing: "banned" and "deactivated" map to those states, everything else is an active traveller.
 * this is a stand-in, not the real Users host; the Users service has its own tests.
 */
internal sealed class UsersService : HttpMessageHandler
{
    internal const string ServiceToken = "journey-service-token";

    private readonly object gate = new();
    private readonly Dictionary<string, Guid> idsBySubject = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> subjectsById = [];

    /**
     * serves identity resolution and account standing for an authorized service caller.
     * @param request the internal Users request from a journey host.
     * @param cancellationToken cancels the request.
     * @return the lookup result, 401 without the service credential, or 404 for an unknown route.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Parameter != ServiceToken) return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/internal/users/identity")
        {
            var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var subject = payload.GetProperty("subject").GetString()!;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = IdFor(subject) }) };
        }

        var parts = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (request.Method == HttpMethod.Get && parts is ["internal", "users", var id, "access"] && Guid.TryParse(id, out var userId))
        {
            string? subject;
            lock (gate)
            {
                subjectsById.TryGetValue(userId, out subject);
            }
            if (subject is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
            var status = subject is "banned" or "deactivated" ? subject : "active";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId, status, role = "traveller" }) };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private Guid IdFor(string subject)
    {
        lock (gate)
        {
            if (idsBySubject.TryGetValue(subject, out var existing)) return existing;
            var created = Guid.CreateVersion7();
            idsBySubject[subject] = created;
            subjectsById[created] = subject;
            return created;
        }
    }
}
