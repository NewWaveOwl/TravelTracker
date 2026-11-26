using System.Net.Http.Headers;
using System.Text.Json;

namespace TravelTracker.Users.Api.Services;

/**
 * Verifies current Supabase email ownership using only the preconfigured HTTPS Auth service.
 * @param http Bounded client with redirects and cookies disabled.
 * @param configuration Server-owned issuer and authority settings.
 * @param logger Records why identity verification failed, never tokens or identities.
 */
public sealed class VerifiedIdentityClient(HttpClient http, IConfiguration configuration, ILogger<VerifiedIdentityClient> logger)
{
    /**
     * Obtains a verified email bound to the same subject as the already-validated access token.
     * @param issuer Validated issuer.
     * @param subject Validated subject.
     * @param accessToken The user token, sent only to the trusted provider's user verification endpoint.
     * @param cancellationToken Request cancellation.
     * @return Verified email, or null when the provider answers without a verified email for this subject. Transport failures and unreadable answers throw, and the host answers them as a dependency failure.
     */
    public async Task<string?> EmailAsync(string issuer, string subject, string accessToken, CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Bearer:Authority"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var provider) || provider.Scheme != Uri.UriSchemeHttps ||
            provider.UserInfo.Length != 0 || provider.Query.Length != 0 || provider.Fragment.Length != 0 ||
            !string.Equals(issuer.TrimEnd('/'), provider.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal))
        {
            logger.LogWarning("Identity verification skipped: Authentication:Bearer:Authority is not a plain HTTPS URL matching the token issuer.");
            return null;
        }
        var key = configuration["Supabase:PublishableKey"];
        if (string.IsNullOrWhiteSpace(key) || !provider.AbsolutePath.TrimEnd('/').EndsWith("/auth/v1", StringComparison.Ordinal))
        {
            logger.LogWarning("Identity verification skipped: the Supabase publishable key is missing or Authority is not an /auth/v1 URL.");
            return null;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(provider.AbsoluteUri.TrimEnd('/') + "/user", UriKind.Absolute));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("apikey", key);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Identity provider user lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            return null;
        }
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        // user_metadata is user editable and cannot establish verified email ownership.
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || id.GetString() != subject ||
            !root.TryGetProperty("email_confirmed_at", out var confirmed) || confirmed.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(confirmed.GetString(), out _) ||
            root.TryGetProperty("is_anonymous", out var anonymous) && anonymous.ValueKind == JsonValueKind.True ||
            !root.TryGetProperty("email", out var verifiedEmail) || verifiedEmail.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(verifiedEmail.GetString())) return null;
        return verifiedEmail.GetString();
    }
}
