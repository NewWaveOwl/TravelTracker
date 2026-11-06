using System.Net;
using System.Net.Http.Headers;

namespace TravelTracker.Settings.Api.Access;

/**
 * resolves current account standing over the authenticated Users service boundary, without caching.
 * @param http the bounded-timeout HTTP client.
 * @param configuration deployment-owned base address and service credential.
 * @param logger safe dependency diagnostics without identity or credential details.
 */
public sealed class UsersAccessClient(HttpClient http, IConfiguration configuration, ILogger<UsersAccessClient> logger)
{
    /**
     * resolves the standing of a validated issuer and subject pair.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param cancellationToken cancels the HTTP lookup.
     * @return live account data; an unknown identity maps to deactivated; null when access cannot be verified.
     */
    public async Task<UserAccessRecord?> ResolveAsync(string issuer, string subject, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(configuration["Users:InternalBaseUrl"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogError("Users access configuration is invalid: an absolute HTTPS base URL is required.");
            return null;
        }
        var credential = configuration["Users:ServiceAccessToken"];
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
            // exception text, URLs, response bodies and caller identifiers may contain secrets.
            logger.LogWarning("Users access lookup failed: {Reason}.", reason);
            return null;
        }
    }

    private sealed record ResolvedUserId(Guid UserId);
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
