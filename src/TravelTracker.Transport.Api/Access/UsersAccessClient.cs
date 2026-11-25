using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TravelTracker.Security.Authentication;

namespace TravelTracker.Transport.Api.Access;

/**
 * Resolves current account standing through the authenticated Users API boundary.
 * @param http The bounded-timeout HTTP client.
 * @param configuration Deployment-owned base address and service credential.
 * @param logger Safe dependency diagnostics without identity or credential details.
 * @param tokenProvider Optional auto-refreshing token provider for Supabase service credentials.
 */
public sealed class UsersAccessClient(HttpClient http, IConfiguration configuration, ILogger<UsersAccessClient> logger, IServiceAccessTokenProvider? tokenProvider = null)
{
    /**
     * Resolves live role and standing for a validated provider identity without caching.
     * @param issuer The caller's validated token issuer.
     * @param subject The caller's validated token subject.
     * @param cancellationToken Cancels the Users requests.
     * @return Current standing and role, deactivated for an unknown account, or null when access cannot be verified.
     */
    public async Task<UserAccessRecord?> ResolveAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["Users:InternalBaseUrl"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            logger.LogError("Users access configuration is invalid: an absolute HTTPS base URL is required.");
            return null;
        }

        var credential = tokenProvider is not null
            ? await tokenProvider.GetTokenAsync(cancellationToken)
            : configuration["Users:ServiceAccessToken"];
        if (string.IsNullOrWhiteSpace(credential))
        {
            logger.LogError("Users access configuration is invalid: a service credential is required.");
            return null;
        }

        try
        {
            var authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var resolve = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "/internal/users/identity"))
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            resolve.Headers.Authorization = authorization;
            using var identity = await http.SendAsync(resolve, cancellationToken);
            if (identity.StatusCode == HttpStatusCode.NotFound) return new UserAccessRecord("deactivated", "traveller");
            if (identity.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users identity lookup returned HTTP {StatusCode}.", (int)identity.StatusCode);
                return null;
            }

            var resolved = await identity.Content.ReadFromJsonAsync<ResolvedUserId>(cancellationToken);
            if (resolved is null || resolved.UserId == Guid.Empty) return null;

            using var lookup = new HttpRequestMessage(HttpMethod.Get,
                new Uri(endpoint, $"/internal/users/{resolved.UserId:D}/access"));
            lookup.Headers.Authorization = authorization;
            using var response = await http.SendAsync(lookup, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return new UserAccessRecord("deactivated", "traveller");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users standing lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var access = await response.Content.ReadFromJsonAsync<ResolvedUserAccess>(cancellationToken);
            return access?.UserId == resolved.UserId &&
                access.Status is "active" or "banned" or "deactivated" &&
                access.Role is "traveller" or "administrator"
                ? new UserAccessRecord(access.Status, access.Role)
                : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or FormatException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var reason = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "transport failure",
                System.Text.Json.JsonException => "invalid JSON",
                _ => "invalid service credential format"
            };
            // Exception text, endpoint details, identity values and credentials are not safe to log.
            logger.LogWarning("Users access lookup failed: {Reason}.", reason);
            return null;
        }
    }

    private sealed record ResolvedUserId(Guid UserId);
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
