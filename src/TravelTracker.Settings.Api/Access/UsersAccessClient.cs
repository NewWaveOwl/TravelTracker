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
    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(5);

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
        // one time budget across both lookups, not one per call
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(LookupBudget);
        // a trailing slash keeps any base path in Users:InternalBaseUrl when relative paths are combined.
        var baseUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        try
        {
            var authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var resolve = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "internal/users/identity"))
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            resolve.Headers.Authorization = authorization;
            using var identity = await http.SendAsync(resolve, budget.Token);
            if (identity.StatusCode == HttpStatusCode.NotFound) return UnknownAccount("identity");
            if (identity.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users identity lookup returned HTTP {StatusCode}.", (int)identity.StatusCode);
                return null;
            }

            var resolved = await identity.Content.ReadFromJsonAsync<ResolvedUserId>(budget.Token);
            if (resolved is null || resolved.UserId == Guid.Empty)
            {
                logger.LogWarning("Users identity lookup returned no usable user id.");
                return null;
            }

            using var lookup = new HttpRequestMessage(HttpMethod.Get,
                new Uri(baseUri, $"internal/users/{resolved.UserId:D}/access"));
            lookup.Headers.Authorization = authorization;
            using var response = await http.SendAsync(lookup, budget.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) return UnknownAccount("standing");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users standing lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }

            var access = await response.Content.ReadFromJsonAsync<ResolvedUserAccess>(budget.Token);
            if (access?.UserId == resolved.UserId &&
                access.Status is "active" or "banned" or "deactivated" &&
                access.Role is "traveller" or "administrator")
                return new UserAccessRecord(access.Status, access.Role);

            logger.LogWarning("Users standing lookup returned a body that does not match the requested user, status or role.");
            return null;
        }
        catch (Exception exception) when ((exception is HttpRequestException or System.Text.Json.JsonException or FormatException or NotSupportedException) ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            var reason = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "transport failure",
                System.Text.Json.JsonException => "invalid JSON",
                NotSupportedException => "unsupported response content type",
                _ => "invalid service credential format"
            };
            // exception text, URLs, response bodies and caller identifiers may contain secrets.
            logger.LogWarning("Users access lookup failed: {Reason}.", reason);
            return null;
        }
    }

    /**
     * maps a Users 404 to a deactivated account. the role is a placeholder because Users returned none, and a deactivated account is denied before any role is used.
     * a wrong Users base path also yields 404, so the refusal is logged to make that visible.
     * @param lookup which Users lookup answered 404.
     * @return the deactivated placeholder record.
     */
    private UserAccessRecord UnknownAccount(string lookup)
    {
        logger.LogWarning("Users {Lookup} lookup returned HTTP 404; treating the account as deactivated.", lookup);
        return new UserAccessRecord("deactivated", "traveller");
    }

    /**
     * the first Users lookup body.
     * @param UserId the internal user id for the validated issuer and subject.
     */
    private sealed record ResolvedUserId(Guid UserId);

    /**
     * the second Users lookup body.
     * @param UserId the internal user id the standing belongs to.
     * @param Status active, banned or deactivated.
     * @param Role traveller or administrator.
     */
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
