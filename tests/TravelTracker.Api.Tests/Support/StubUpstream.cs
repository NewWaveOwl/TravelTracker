using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Api.Tests.Support;

/**
 * Stands in for the other services over HTTP. Each route answers with a scripted response, and every
 * call is recorded so a test can prove which upstream services were reached and with which credential.
 * An unscripted call fails the test instead of passing silently.
 */
public sealed class StubUpstream : HttpMessageHandler {
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes = [];
    private readonly HashSet<string> stalls = [];

    /** Every request that reached the stub, as "METHOD path". */
    public List<string> Calls { get; } = [];

    /** Authorization header sent with each call, as "METHOD path" to header value. */
    public Dictionary<string, string?> Credentials { get; } = [];

    /**
     * Scripts a JSON response for one route.
     *
     * @param method HTTP method, such as GET.
     * @param path Absolute path, such as /api/v1/accommodations/{id}.
     * @param status Status code to return.
     * @param body Optional body, serialized with web defaults.
     */
    public void Respond(string method, string path, HttpStatusCode status, object? body = null) {
        stalls.Remove(Key(method, path));
        routes[Key(method, path)] = _ => new HttpResponseMessage(status) {
            Content = body is null ? null : JsonContent.Create(body, options: JsonSerializerOptions.Web)
        };
    }

    /**
     * Scripts a transport-level failure for one route, such as a timeout or refused connection.
     *
     * @param method HTTP method.
     * @param path Absolute path.
     * @param exception The exception the handler throws.
     */
    public void Fail(string method, string path, Exception exception) {
        stalls.Remove(Key(method, path));
        routes[Key(method, path)] = _ => throw exception;
    }

    /**
     * Scripts a route that never answers until the caller's cancellation token fires, so the client's own timeout is what ends it.
     *
     * @param method HTTP method.
     * @param path Absolute path.
     */
    public void Stall(string method, string path) => stalls.Add(Key(method, path));

    /**
     * Records the call and answers it from the script.
     *
     * @param request The outgoing request.
     * @param cancellationToken Cancelled by the client when its deadline passes; only stalled routes observe it.
     * @return The scripted response.
     */
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var key = Key(request.Method.Method, request.RequestUri!.AbsolutePath);
        Calls.Add(key);
        Credentials[key] = request.Headers.Authorization?.ToString();
        if (stalls.Contains(key)) {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("a stalled route must not complete.");
        }
        if (!routes.TryGetValue(key, out var route)) throw new InvalidOperationException($"unscripted upstream call {key}.");
        return route(request);
    }

    /**
     * Builds the lookup key for a route.
     *
     * @param method HTTP method.
     * @param path Absolute path.
     * @return The key used by Calls and the route table.
     */
    private static string Key(string method, string path) => $"{method} {path}";
}
