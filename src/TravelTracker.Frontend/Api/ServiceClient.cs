using System.Net.Http.Headers;
using System.Text.Json;

namespace TravelTracker.Frontend.Api;

/**
 * Reads public JSON HTTP contracts with bounded waiting and cancellation.
 *
 * @param httpClient The client used for outbound requests.
 * @param logger Receives every failure that is turned into an ApiError.
 */
public sealed class ServiceClient(HttpClient httpClient, ILogger<ServiceClient> logger)
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /**
     * Requests a public JSON resource.
     *
     * @param url The public service URL.
     * @param cancellationToken Cancels work when the consuming view closes.
     * @return The parsed response; throws a categorized ApiError on failure and OperationCanceledException when the caller cancelled.
     */
    public async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await httpClient.SendAsync(request, bounded.Token);
            if (!response.IsSuccessStatusCode)
                throw Fail(Classify(response.StatusCode), "The request could not be completed.", url, (int)response.StatusCode);
            return await response.Content.ReadFromJsonAsync<T>(bounded.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw Fail(FailureKind.Network, "Check your connection and try again.", url, null, exception);
        }
        catch (JsonException exception)
        {
            throw Fail(FailureKind.Unexpected, "The service returned an unreadable response.", url, null, exception);
        }
    }

    static FailureKind Classify(System.Net.HttpStatusCode status) => (int)status switch
    {
        400 or 422 => FailureKind.Validation,
        401 or 403 => FailureKind.Permission,
        >= 500 => FailureKind.Unavailable,
        _ => FailureKind.Unexpected,
    };

    ApiError Fail(FailureKind kind, string message, string url, int? status, Exception? cause = null)
    {
        logger.LogWarning(cause, "GET {Url} failed as {Kind} (status {Status})", url, kind, status);
        return new ApiError(kind, message, status);
    }
}
