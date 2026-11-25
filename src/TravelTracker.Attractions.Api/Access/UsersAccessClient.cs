using System.Net;
using System.Net.Http.Headers;
using TravelTracker.Security.Authentication;

namespace TravelTracker.Attractions.Api.Access;

/**
 * Resolves account standing over the documented Users HTTP boundary without caching.
 * @param httpClient The bounded-timeout HTTP client.
 * @param configuration Deployment-owned endpoint and service credential.
 * @param logger Safe dependency diagnostics without identity or credential details.
 * @param tokenProvider Optional auto-refreshing token provider for Supabase service credentials.
 */
public sealed class UsersAccessClient(HttpClient httpClient, IConfiguration configuration, ILogger<UsersAccessClient> logger, IServiceAccessTokenProvider? tokenProvider = null)
{
    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(5);
    private const string IdentityPath = "internal/users/identity";
    private const string AccessPathFormat = "internal/users/{0:D}/access";

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
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            logger.LogError("Users access configuration is invalid: an absolute HTTPS endpoint is required.");
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
        // one time budget across both lookups, not one per call
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(LookupBudget);
        try
        {
            var authorization = new AuthenticationHeaderValue("Bearer", credential);
            if (useScopedLookup)
                return await ResolveByPublicIdAsync(endpoint, issuer, subject, authorization, budget.Token);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            request.Headers.Authorization = authorization;
            using var response = await httpClient.SendAsync(request, budget.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users access lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return null;
            }
            var access = await response.Content.ReadFromJsonAsync<UserAccessRecord>(budget.Token);
            if (access?.Status is "active" or "banned" or "deactivated" &&
                access.Role is "traveller" or "administrator") return access;
            logger.LogWarning("Users access lookup returned an invalid status or role.");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or FormatException or NotSupportedException ||
            exception is InvalidOperationException && exception.InnerException is ArgumentException ||
            exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var reason = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "transport failure",
                System.Text.Json.JsonException => "invalid JSON",
                NotSupportedException => "unsupported response content type",
                FormatException => "invalid service credential format",
                // an unreadable response charset surfaces as InvalidOperationException wrapping ArgumentException
                _ => "invalid response encoding"
            };
            // exception text, URLs, response bodies and caller identifiers may contain secrets.
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
        // a trailing slash keeps any base path in Users:InternalBaseUrl when relative paths are combined.
        var baseUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        using var resolve = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, IdentityPath))
        {
            Content = JsonContent.Create(new { issuer, subject })
        };
        resolve.Headers.Authorization = authorization;
        using var identity = await httpClient.SendAsync(resolve, cancellationToken);
        if (identity.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Users identity lookup found no account; treating the caller as deactivated.");
            return new UserAccessRecord("deactivated", "traveller");
        }
        if (identity.StatusCode != HttpStatusCode.OK)
        {
            logger.LogWarning("Users identity lookup returned HTTP {StatusCode}.", (int)identity.StatusCode);
            return null;
        }

        var resolved = await identity.Content.ReadFromJsonAsync<ResolvedUserId>(cancellationToken);
        if (resolved is null || resolved.UserId == Guid.Empty)
        {
            logger.LogWarning("Users identity lookup returned an empty user identifier.");
            return null;
        }

        using var lookup = new HttpRequestMessage(HttpMethod.Get,
            new Uri(baseUri, string.Format(System.Globalization.CultureInfo.InvariantCulture, AccessPathFormat, resolved.UserId)));
        lookup.Headers.Authorization = authorization;
        using var response = await httpClient.SendAsync(lookup, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation("Users standing lookup found no account; treating the caller as deactivated.");
            return new UserAccessRecord("deactivated", "traveller");
        }
        if (response.StatusCode != HttpStatusCode.OK)
        {
            logger.LogWarning("Users standing lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }

        var access = await response.Content.ReadFromJsonAsync<ResolvedUserAccess>(cancellationToken);
        if (access?.UserId == resolved.UserId &&
            access.Status is "active" or "banned" or "deactivated" &&
            access.Role is "traveller" or "administrator")
            return new UserAccessRecord(access.Status, access.Role);
        logger.LogWarning("Users standing lookup returned a mismatched user, status or role.");
        return null;
    }

    private sealed record ResolvedUserId(Guid UserId);
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
