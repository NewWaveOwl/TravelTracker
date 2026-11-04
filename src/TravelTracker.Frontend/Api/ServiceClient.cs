using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace TravelTracker.Frontend.Api;

/**
 * Exchanges JSON HTTP contracts with bounded waiting; cookies and redirects are never followed.
 * @param httpClient The client used for outbound requests.
 * @param logger Receives failure categories, never tokens, request bodies or problem diagnostics.
 */
public sealed class ServiceClient(HttpClient httpClient, ILogger<ServiceClient> logger)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    static readonly HashSet<string> WritableFields = new(StringComparer.OrdinalIgnoreCase)
    { "name", "address", "lat", "lon", "estimatedVisitMinutes", "rating", "description", "visibility", "code", "type", "sourceId" };

    /**
     * Reads a public JSON contract.
     * @param url Destination URL.
     * @param cancellationToken Cancels when the view closes.
     * @param accessToken Optional token supplied only by ProtectedClient.
     * @return Parsed response or a categorized failure.
     */
    public Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken = default, string? accessToken = null) =>
        SendJsonAsync<T>(HttpMethod.Get, url, null, cancellationToken, accessToken);

    /**
     * Sends one JSON request without retrying writes whose outcome may be unknown.
     * @param method GET, POST or PATCH.
     * @param url Validated destination.
     * @param body Public writable contract, or null for a read.
     * @param cancellationToken Cancels work when the consuming view closes.
     * @param accessToken Optional bearer supplied by ProtectedClient.
     * @param bootstrapSecret Optional server-held first-administrator secret, restricted to the exact HTTPS bootstrap endpoint.
     * @return Parsed response; throws ApiError or caller cancellation.
     */
    public async Task<T?> SendJsonAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken = default, string? accessToken = null, string? bootstrapSecret = null)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Patch)
            throw new ApiError(FailureKind.Unexpected, "Unsupported request method.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (bootstrapSecret is not null)
        {
            var destination = new Uri(url);
            if (method != HttpMethod.Post || destination.Scheme != Uri.UriSchemeHttps || destination.AbsolutePath != "/api/v1/users/bootstrap-first-administrator" || destination.Query.Length != 0 || destination.Fragment.Length != 0)
                throw new ApiError(FailureKind.Unexpected, "Invalid administrator setup destination.");
            request.Headers.Add("X-Bootstrap-Secret", bootstrapSecret);
        }
        try
        {
            using var response = await httpClient.SendAsync(request, bounded.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var errors = status is 400 or 422 ? await FieldErrorsAsync(response, bounded.Token) : null;
                throw Fail(Classify(status), MessageFor(status), method, status, errors);
            }
            return await response.Content.ReadFromJsonAsync<T>(bounded.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw Fail(FailureKind.Network, method == HttpMethod.Get ? "Check your connection and try again." : "The save could not be confirmed. Check the catalogue before retrying to avoid creating a duplicate.", method);
        }
        catch (JsonException)
        {
            throw Fail(FailureKind.Unexpected, method == HttpMethod.Get ? "The service returned an unreadable response." : "The save response could not be read. Check the catalogue before retrying.", method);
        }
    }

    static async Task<IReadOnlyDictionary<string, string[]>> FieldErrorsAsync(HttpResponseMessage response, CancellationToken token)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("errors", out var fields) && fields.ValueKind == JsonValueKind.Object)
                foreach (var field in fields.EnumerateObject())
                    if (WritableFields.Contains(field.Name) && field.Value.ValueKind == JsonValueKind.Array)
                        result[char.ToLowerInvariant(field.Name[0]) + field.Name[1..]] = field.Value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String)
                            .Take(4).Select(v => v.GetString()!).Where(s => s.Length <= 300).ToArray();
        }
        catch (JsonException) { /* Non-JSON failures still have a usable status-based message. */ }
        return result;
    }

    static FailureKind Classify(int status) => status switch
    {
        400 or 422 => FailureKind.Validation,
        401 or 403 => FailureKind.Permission,
        >= 500 => FailureKind.Unavailable,
        _ => FailureKind.Unexpected,
    };

    static string MessageFor(int status) => status switch
    {
        400 or 422 => "Check the highlighted attraction fields and try again.",
        404 => "This attraction is no longer available. Refresh the catalogue.",
        409 or 412 => "This attraction changed while you were editing. Review the latest record before saving again.",
        >= 500 => "The requested service is unavailable. Your entries are retained; try again shortly.",
        _ => "The request could not be completed.",
    };

    ApiError Fail(FailureKind kind, string message, HttpMethod method, int? status = null, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        logger.LogWarning("{Method} service request failed as {Kind} (status {Status})", method, kind, status);
        return new ApiError(kind, message, status, errors);
    }
}
