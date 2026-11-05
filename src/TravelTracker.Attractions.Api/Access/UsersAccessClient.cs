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
        var internalBase = configuration["Users:InternalBaseUrl"];
        var useScopedLookup = !string.IsNullOrWhiteSpace(internalBase);
        if (!Uri.TryCreate(useScopedLookup ? internalBase : configuration["Users:AccessEndpoint"], UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
        {
            logger.LogError("Users access configuration is invalid: an absolute HTTPS endpoint is required.");
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
            if (useScopedLookup)
                return await ResolveByPublicIdAsync(endpoint, issuer, subject, authorization, cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            request.Headers.Authorization = authorization;
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

    /**
     * Resolves the validated identity over the trusted backchannel and then checks current standing by ID.
     * @param endpoint The configured Users HTTPS origin.
     * @param issuer The validated caller issuer.
     * @param subject The validated caller subject.
     * @param authorization The authorized backend service credential.
     * @param cancellationToken Cancels both lookups.
     * @return Account standing, or null when a dependency error prevents a decision.
     */
    private async Task<UserAccessRecord?> ResolveByPublicIdAsync(Uri endpoint, string issuer, string subject,
        AuthenticationHeaderValue authorization, CancellationToken cancellationToken)
    {
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

        using var lookup = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, $"/internal/users/{resolved.UserId:D}/access"));
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

    private sealed record ResolvedUserId(Guid UserId);
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
