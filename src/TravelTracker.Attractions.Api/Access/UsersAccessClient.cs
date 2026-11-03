using System.Net;
using System.Net.Http.Headers;

namespace TravelTracker.Attractions.Api.Access;

/**
 * Resolves account standing over the documented Users HTTP boundary without caching.
 * @param http The bounded-timeout HTTP client.
 * @param configuration Deployment-owned endpoint and service credential.
 * @param logger Safe dependency diagnostics without identity or credential details.
 */
public sealed class UsersAccessClient(HttpClient http, IConfiguration configuration, ILogger<UsersAccessClient> logger)
{
    /**
     * Obtains current standing for a validated issuer/subject pair.
     * @param issuer The caller's validated token issuer.
     * @param subject The caller's validated subject.
     * @param cancellationToken Cancels the lookup.
     * @return Live account data, or null when the dependency is unavailable/misconfigured.
     */
    public async Task<UserAccessRecord?> ResolveAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["Users:AccessEndpoint"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogError("Users access configuration is invalid: an absolute HTTPS endpoint is required.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(configuration["Users:ServiceAccessToken"]))
        {
            logger.LogError("Users access configuration is invalid: a service credential is required.");
            return null;
        }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["Users:ServiceAccessToken"]);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users access lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }
            var access = await response.Content.ReadFromJsonAsync<UserAccessRecord>(cancellationToken);
            if (access?.Status is "active" or "banned" or "deactivated" &&
                access.Role is "traveller" or "administrator") return access;
            logger.LogWarning("Users access lookup returned an invalid status or role.");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or FormatException ||
            exception is InvalidOperationException && exception.InnerException is ArgumentException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var reason = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "transport failure",
                System.Text.Json.JsonException => "invalid JSON",
                FormatException => "invalid service credential format",
                _ => "invalid response encoding"
            };
            // Exception text, URLs, response bodies and caller identifiers may contain secrets.
            logger.LogWarning("Users access lookup failed: {Reason}.", reason);
            return null;
        }
    }
}
