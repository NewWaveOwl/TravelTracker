using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TravelTracker.Accommodations.Api.Access;

/** Resolves current account standing over the authenticated Users service boundary. */
public sealed class UsersAccessClient(HttpClient httpClient, IConfiguration configuration, ILogger<UsersAccessClient> logger)
{
    private static readonly TimeSpan LookupBudget = TimeSpan.FromSeconds(5);

    /**
     * Resolves the standing of a validated issuer/subject pair without caching.
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param cancellationToken Cancels the HTTP lookup.
     * @return Live account data, or null when access cannot be verified.
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
        var baseUri = new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        try
        {
            var authorization = new AuthenticationHeaderValue("Bearer", credential);
            using var resolve = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "internal/users/identity"))
            {
                Content = JsonContent.Create(new { issuer, subject })
            };
            resolve.Headers.Authorization = authorization;
            using var identity = await httpClient.SendAsync(resolve, budget.Token);
            if (identity.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("Users does not know this identity; treating the account as deactivated.");
                return new UserAccessRecord("deactivated", "traveller");
            }
            if (identity.StatusCode != HttpStatusCode.OK)
            {
                logger.LogWarning("Users identity lookup returned HTTP {StatusCode}.", (int)identity.StatusCode);
                return null;
            }

            var resolved = await identity.Content.ReadFromJsonAsync<ResolvedUserId>(budget.Token);
            if (resolved is null || resolved.UserId == Guid.Empty)
            {
                logger.LogWarning("Users identity lookup returned no usable user identifier.");
                return null;
            }

            using var lookup = new HttpRequestMessage(HttpMethod.Get,
                new Uri(baseUri, $"internal/users/{resolved.UserId:D}/access"));
            lookup.Headers.Authorization = authorization;
            using var response = await httpClient.SendAsync(lookup, budget.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogInformation("Users has no standing record for this account; treating it as deactivated.");
                return new UserAccessRecord("deactivated", "traveller");
            }
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

            logger.LogWarning("Users standing lookup returned a mismatched user identifier, status or role.");
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or FormatException or NotSupportedException ||
            (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            var reason = exception switch
            {
                OperationCanceledException => "timeout",
                HttpRequestException => "transport failure",
                JsonException => "invalid JSON",
                NotSupportedException => "unsupported response content type",
                _ => "invalid service credential format"
            };
            // do not log exception text, URLs, response bodies or caller identifiers.
            logger.LogWarning("Users access lookup failed: {Reason}.", reason);
            return null;
        }
    }

    private sealed record ResolvedUserId(Guid UserId);
    private sealed record ResolvedUserAccess(Guid UserId, string Status, string Role);
}
