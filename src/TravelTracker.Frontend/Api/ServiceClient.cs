using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TravelTracker.Frontend.Api;

/**
 * Exchanges JSON and bounded image HTTP contracts with bounded waiting; cookies and redirects are never followed.
 * @param httpClient The client used for outbound requests.
 * @param logger Receives failure categories, never tokens, request bodies or problem diagnostics.
 */
public sealed partial class ServiceClient(HttpClient httpClient, ILogger<ServiceClient> logger)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    static readonly HashSet<string> WritableFields = new(StringComparer.OrdinalIgnoreCase)
    { "name", "address", "lat", "lon", "estimatedVisitMinutes", "rating", "description", "visibility", "code", "type", "sourceId", "username", "theme", "defaultMapLatitude", "defaultMapLongitude", "defaultMapZoom", "preferredTransportMode", "timeZone", "currentPassword", "newPassword" };

    /**
     * Reads a public JSON contract.
     * @param url Destination URL.
     * @param cancellationToken Cancels when the view closes.
     * @return Parsed response or a categorized failure.
     */
    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken = default) =>
        (await SendAsync<T>(HttpMethod.Get, url, null, cancellationToken)).Body;

    /**
     * Sends one JSON request and keeps the response entity tag, which optimistic-concurrency writes need.
     * @param method GET, POST, PUT or PATCH.
     * @param url Validated destination.
     * @param body Public writable contract, or null for a read.
     * @param cancellationToken Cancels work when the consuming view closes.
     * @param accessToken Optional bearer supplied by ProtectedClient.
     * @param bootstrapSecret Optional server-held first-administrator secret, restricted to the exact HTTPS bootstrap endpoint.
     * @param ifMatch Optional entity tag the server must match before it applies a replace.
     * @return Parsed body and entity tag; throws ApiError or caller cancellation.
     */
    public async Task<ServiceResponse<T>> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken = default, string? accessToken = null, string? bootstrapSecret = null, string? ifMatch = null)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Post && method != HttpMethod.Put && method != HttpMethod.Patch)
            throw new ApiError(FailureKind.Unexpected, "Unsupported request method.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        if (bootstrapSecret is not null)
        {
            var destination = new Uri(url);
            if (method != HttpMethod.Post || destination.Scheme != Uri.UriSchemeHttps || destination.AbsolutePath != "/api/v1/users/bootstrap-first-administrator" || destination.Query.Length != 0 || destination.Fragment.Length != 0)
                throw new ApiError(FailureKind.Unexpected, "Invalid administrator setup destination.");
            request.Headers.Add("X-Bootstrap-Secret", bootstrapSecret);
        }
        try
        {
            // headers first, so a body that fails mid-read is handled where the status is still known
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var trips = request.RequestUri!.AbsolutePath.StartsWith("/api/v1/trips", StringComparison.Ordinal);
                var errors = status is 400 or 422 || (trips && status == 404)
                    ? await FieldErrorsAsync(response, trips, bounded.Token) : null;
                var code = status == 503 && (request.RequestUri.AbsolutePath is "/v1/places/search" or "/v1/places/reverse" or "/v1/routes/plan" or "/v1/routes/capabilities")
                    ? await RoutingFailureCodeAsync(response, bounded.Token, cancellationToken) : null;
                var message = code == "authentication_unavailable"
                    ? "TravelTracker authentication is unavailable on the routing server. Check that its TravelTracker Supabase URL and key match this app. Manual planning remains available."
                    : trips && status == 404 ? "This trip is no longer available. Refresh your trips." : MessageFor(status);
                throw Fail(Classify(status), message, method, status, errors, ResourceOf(request), code: code);
            }
            // a confirmed 204 has no body to read, so it reports no value rather than failing to parse
            var parsed = response.StatusCode == System.Net.HttpStatusCode.NoContent ? default : await response.Content.ReadFromJsonAsync<T>(bounded.Token);
            return new ServiceResponse<T>(parsed, response.Headers.ETag?.Tag);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw Fail(FailureKind.Network, method == HttpMethod.Get ? "Check your connection and try again." : "The save could not be confirmed. Check the saved record before retrying to avoid creating a duplicate.", method, resource: ResourceOf(request), exception: exception);
        }
        // ReadFromJsonAsync reports an unusable charset or body with these exception types.
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw Fail(FailureKind.Unexpected, method == HttpMethod.Get ? "The service returned an unreadable response." : "The save response could not be read. Check the saved record before retrying.", method, resource: ResourceOf(request), exception: exception);
        }
    }

    public Task<T?> ParseTicketTextAsync<T>(string url, string text, CancellationToken cancellationToken, string accessToken) =>
        SendTicketAsync(HttpMethod.Post, url, new StringContent(text, System.Text.Encoding.UTF8, "text/plain"), null,
            cancellationToken, accessToken, (response, token) => response.Content.ReadFromJsonAsync<T>(token));

    /**
     * Uploads one PDF directly, without multipart buffering or retrying an uncertain upload.
     * @param url Validated Trips destination.
     * @param content The caller-owned PDF stream.
     * @param fileName A display filename, never a server path.
     * @param cancellationToken Cancels a closed import view.
     * @param accessToken Session bearer supplied by ProtectedClient.
     * @return The extraction draft returned by the service.
     */
    public Task<T?> UploadPdfAsync<T>(string url, Stream content, string fileName, CancellationToken cancellationToken, string accessToken)
    {
        var body = new StreamContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        return SendTicketAsync(HttpMethod.Post, url, body, fileName, cancellationToken, accessToken,
            (response, token) => response.Content.ReadFromJsonAsync<T>(token));
    }

    /**
     * Reads a bounded private PDF for the authenticated frontend download endpoint.
     * @param url Validated Trips destination.
     * @param cancellationToken Cancels a closed download.
     * @param accessToken Session bearer supplied by ProtectedClient.
     * @return At most ten MiB of PDF bytes, or a categorized failure.
     */
    public Task<byte[]> GetPdfAsync(string url, CancellationToken cancellationToken, string accessToken) =>
        SendTicketAsync(HttpMethod.Get, url, null, null, cancellationToken, accessToken, async (response, token) =>
        {
            const int maximum = 10 * 1024 * 1024;
            if (response.Content.Headers.ContentType?.MediaType != "application/pdf" || response.Content.Headers.ContentLength > maximum)
                throw new ApiError(FailureKind.Unexpected, "The service returned an unsupported document.");
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var block = new byte[16384];
            try
            {
                int count;
                while ((count = await source.ReadAsync(block.AsMemory(0, Math.Min(block.Length, maximum + 1 - (int)buffer.Length)), token)) != 0)
                {
                    await buffer.WriteAsync(block.AsMemory(0, count), token);
                    if (buffer.Length > maximum) throw new ApiError(FailureKind.Unexpected, "The service returned an oversized document.");
                }
                return buffer.ToArray();
            }
            finally
            {
                Array.Clear(block);
                if (buffer.TryGetBuffer(out var retained)) Array.Clear(retained.Array!, retained.Offset, retained.Count);
            }
        });

    /**
     * Removes an owner's temporary import without trying to parse a no-content response.
     * @param url Validated Trips destination.
     * @param cancellationToken Cancels cleanup.
     * @param accessToken Session bearer supplied by ProtectedClient.
     * @return Completion after the service acknowledges removal.
     */
    public async Task DeleteTicketAsync(string url, CancellationToken cancellationToken, string accessToken) =>
        _ = await SendTicketAsync(HttpMethod.Delete, url, null, null, cancellationToken, accessToken, (_, _) => Task.FromResult(true));

    async Task<T> SendTicketAsync<T>(HttpMethod method, string url, HttpContent? content, string? fileName,
        CancellationToken cancellationToken, string accessToken, Func<HttpResponseMessage, CancellationToken, Task<T>> read)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(method == HttpMethod.Get ? "application/pdf" : "application/json"));
        if (fileName is not null)
            request.Headers.Add("X-Ticket-File-Name", Uri.EscapeDataString(fileName));
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (!response.IsSuccessStatusCode) throw Fail(Classify((int)response.StatusCode), MessageFor((int)response.StatusCode), method, (int)response.StatusCode);
            return await read(response, bounded.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw Fail(FailureKind.Network, "The ticket service could not be reached. Your trip is unchanged.", method);
        }
        catch (JsonException)
        {
            throw Fail(FailureKind.Unexpected, "The ticket response could not be read. Your trip is unchanged.", method);
        }
    }

    /**
     * Exchanges a bounded raw image contract without following a returned Location or logging image data.
     * @param method GET for a private PNG or PUT for raw PNG/JPEG bytes.
     * @param url The configured, validated destination.
     * @param body Upload bytes, or null for a read.
     * @param contentType The upload media type, or null for a read.
     * @param maxResponseBytes Independent cap for a normalized PNG response.
     * @param accessToken Bearer supplied by ProtectedClient, never an image URL parameter.
     * @param cancellationToken Cancels the consuming view's work.
     * @return The bounded PNG bytes for GET, or an empty array after a confirmed 204 PUT.
     */
    public async Task<byte[]> SendImageAsync(HttpMethod method, string url, byte[]? body, string? contentType,
        int maxResponseBytes, string accessToken, CancellationToken cancellationToken = default)
    {
        if (method != HttpMethod.Get && method != HttpMethod.Put || maxResponseBytes is < 1 or > 2 * 1024 * 1024)
            throw new ApiError(FailureKind.Unexpected, "Unsupported image request.");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType!);
        }
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw Fail(Classify(status), MessageFor(status), method, status, resource: ResourceOf(request));
            }
            if (method == HttpMethod.Put)
            {
                if (response.StatusCode != System.Net.HttpStatusCode.NoContent)
                    throw Fail(FailureKind.Unexpected, "The service did not confirm the photo upload.", method, resource: ResourceOf(request));
                return [];
            }
            if (response.StatusCode != System.Net.HttpStatusCode.OK ||
                !string.Equals(response.Content.Headers.ContentType?.MediaType, "image/png", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentLength > maxResponseBytes)
                throw Fail(FailureKind.Unexpected, "The service returned an unreadable profile picture.", method, resource: ResourceOf(request));
            var buffer = new byte[maxResponseBytes + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(bounded.Token);
            var count = await stream.ReadAtLeastAsync(buffer, buffer.Length, false, bounded.Token);
            if (count > maxResponseBytes || count < 8 || !buffer.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw Fail(FailureKind.Unexpected, "The service returned an unreadable profile picture.", method, resource: ResourceOf(request));
            return buffer[..count];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            throw Fail(FailureKind.Network, method == HttpMethod.Get ? "The profile picture could not be loaded. Try again."
                : "The photo upload could not be confirmed. Reload the picture before trying another upload.", method,
                resource: ResourceOf(request), exception: exception);
        }
    }

    async Task<IReadOnlyDictionary<string, string[]>> FieldErrorsAsync(HttpResponseMessage response, bool trips, CancellationToken token)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("errors", out var fields) && fields.ValueKind == JsonValueKind.Object)
                foreach (var field in fields.EnumerateObject())
                    if ((WritableFields.Contains(field.Name) || (trips && TripField().IsMatch(field.Name))) && field.Value.ValueKind == JsonValueKind.Array)
                        result[char.ToLowerInvariant(field.Name[0]) + field.Name[1..]] = field.Value.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                            .Select(value => value.GetString()!).Where(text => text.Length <= 300).Take(4).ToArray();
        }
        catch (JsonException exception)
        {
            // non-JSON failures still have a usable status-based message
            logger.LogWarning("Field errors in a failure response were not valid JSON; the status-based message is shown instead ({FailureType})", exception.GetType().Name);
        }
        catch (HttpRequestException exception)
        {
            // the connection dropped while the error body was read; the status still tells the caller what failed
            logger.LogWarning("Field errors in a failure response could not be read; the status-based message is shown instead ({FailureType})", exception.GetType().Name);
            return new Dictionary<string, string[]>();
        }
        return result;
    }

    /**
     * Recognizes one public routing setup code in a small JSON failure without forwarding response messages.
     * @param response The routing endpoint's known 503 response.
     * @param token The existing request deadline, also bounding failure-body reads.
     * @param cancellationToken The caller cancellation which must remain observable.
     * @return The allowlisted code, or null for oversized, malformed, unreadable or unrecognized bodies.
     */
    static async Task<string?> RoutingFailureCodeAsync(HttpResponseMessage response, CancellationToken token, CancellationToken cancellationToken)
    {
        const int maximum = 4096;
        if (response.Content.Headers.ContentLength > maximum ||
            !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var bytes = new byte[maximum + 1];
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            var count = await stream.ReadAtLeastAsync(bytes, bytes.Length, false, token);
            if (count > maximum) return null;
            using var body = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 4 });
            return body.RootElement.ValueKind == JsonValueKind.Object && body.RootElement.TryGetProperty("status", out var status) &&
                status.ValueKind == JsonValueKind.String && status.GetString() == "authentication_unavailable" ? "authentication_unavailable" : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception exception) when (exception is JsonException or HttpRequestException or IOException) { return null; }
    }

    [GeneratedRegex(@"\A(?:start|end|isRoundTrip|stops(?:\[\d+\](?:\.(?:type|placeId))?)?|legs(?:\[\d+\](?:\.(?:sequence|from(?:\.(?:type|placeId))?|to(?:\.(?:type|placeId))?|transport(?:ModeId)?|departure|arrival))?)?)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TripField();

    static FailureKind Classify(int status) => status switch
    {
        400 or 422 => FailureKind.Validation,
        401 or 403 => FailureKind.Permission,
        >= 500 => FailureKind.Unavailable,
        _ => FailureKind.Unexpected,
    };

    static string MessageFor(int status) => status switch
    {
        400 or 422 => "Check the submitted details and try again.",
        404 => "This record is no longer available. Reload the page to see the current records.",
        409 or 412 => "This record changed while you were editing. Review the latest version before saving again.",
        >= 500 => "The requested service is unavailable. Your entries are retained; try again shortly.",
        _ => "The request could not be completed.",
    };

    // first three path segments only (for example /api/v1/trips), so record identifiers never reach the log
    static string ResourceOf(HttpRequestMessage request) => string.Concat(request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Take(3).Select(segment => "/" + segment));

    /**
     * Logs a failed request by route and category, then builds the error the view shows.
     * @param kind The failure category the view branches on.
     * @param message The fixed message the view shows.
     * @param method The HTTP method that failed.
     * @param status The response status, when the server answered.
     * @param errors Field errors the server listed, when present.
     * @param resource The route-only resource name, never record identifiers.
     * @param exception The underlying cause, represented in the log only by its type.
     * @param code Optional allowlisted machine code used for fixed recovery guidance.
     * @return The error to throw.
     */
    ApiError Fail(FailureKind kind, string message, HttpMethod method, int? status = null, IReadOnlyDictionary<string, string[]>? errors = null, string? resource = null, Exception? exception = null, string? code = null)
    {
        // a 404 is an expected answer for lookups (for example a first sign-in with no account yet), so it is not a warning
        logger.Log(status == 404 ? LogLevel.Information : LogLevel.Warning,
            "{Method} service request to {Resource} failed as {Kind} (status {Status}, failure type {FailureType})",
            method, resource, kind, status, exception?.GetType().Name);
        return new ApiError(kind, message, status, errors, code);
    }
}
