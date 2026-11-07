using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Trips.Api.Tests.Support;

/**
 * Stands in for the Users and catalogue services over HTTP. Users answers from scripted accounts, and every catalogue
 * route answers from a script. Every call and the credential it carried is recorded; an unscripted call fails the request.
 */
internal sealed class TripUpstream : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes = [];
    private readonly Dictionary<string, (Guid UserId, string Status, string Role)> accounts = new(StringComparer.Ordinal);

    /** Every request that reached the stub, as "METHOD path". */
    internal List<string> Calls { get; } = [];

    /** Authorization header value sent with each call, keyed by "METHOD path". */
    internal Dictionary<string, string?> Credentials { get; } = [];

    /** When true, every Users call answers 503, as if Users were down. */
    internal bool UsersUnavailable { get; set; }

    /**
     * Registers a Users account for a subject, with the identifier Users would assign.
     *
     * @param subject Validated token subject.
     * @param userId Public account identifier.
     * @param status active, banned or deactivated.
     * @param role traveller or administrator.
     */
    internal void Account(string subject, Guid userId, string status, string role) => accounts[subject] = (userId, status, role);

    /**
     * Scripts a JSON response for one catalogue route.
     *
     * @param method HTTP method.
     * @param path Absolute path, such as /api/v1/accommodations/{id}.
     * @param status Status code to return.
     * @param body Optional body, serialized with web defaults.
     */
    internal void Respond(string method, string path, HttpStatusCode status, object? body = null) =>
        routes[Key(method, path)] = _ => new HttpResponseMessage(status)
        {
            Content = body is null ? null : JsonContent.Create(body, options: JsonSerializerOptions.Web)
        };

    /**
     * Answers a Users or catalogue call from the scripts and records it.
     *
     * @param request The outgoing request.
     * @param cancellationToken Cancels the call.
     * @return The scripted response.
     * @throws InvalidOperationException When no script exists for the call.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = Key(request.Method.Method, request.RequestUri!.AbsolutePath);
        Calls.Add(key);
        Credentials[key] = request.Headers.Authorization?.ToString();

        if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/internal/users/identity")
            return await IdentityAsync(request, cancellationToken);
        var parts = request.RequestUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (request.Method == HttpMethod.Get && parts is { Length: 4 } && parts[0] == "internal" && parts[1] == "users" && parts[3] == "access")
            return AccessFor(parts[2]);
        if (routes.TryGetValue(key, out var route)) return route(request);
        throw new InvalidOperationException($"unscripted upstream call {key}.");
    }

    /**
     * Resolves a validated issuer and subject to a scripted account identifier.
     *
     * @param request The identity lookup.
     * @param cancellationToken Cancels reading the body.
     * @return 200 with the identifier, 404 for an unknown subject, or 503 when Users is down.
     */
    private async Task<HttpResponseMessage> IdentityAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (UsersUnavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var payload = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var subject = payload.GetProperty("subject").GetString() ?? string.Empty;
        return accounts.TryGetValue(subject, out var account)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { userId = account.UserId }) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /**
     * Returns the live standing of a scripted account.
     *
     * @param userIdText The account identifier from the path.
     * @return 200 with status and role, 404 when unknown, or 503 when Users is down.
     */
    private HttpResponseMessage AccessFor(string userIdText)
    {
        if (UsersUnavailable) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        if (!Guid.TryParse(userIdText, out var userId)) return new HttpResponseMessage(HttpStatusCode.NotFound);
        foreach (var account in accounts.Values)
        {
            if (account.UserId != userId) continue;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { userId, status = account.Status, role = account.Role })
            };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /**
     * Builds the lookup key for a route.
     *
     * @param method HTTP method.
     * @param path Absolute path.
     * @return The key used by Calls, Credentials and the route table.
     */
    private static string Key(string method, string path) => $"{method} {path}";
}
