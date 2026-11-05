using System.Net.Http.Headers;
using System.Text.Json;

namespace TravelTracker.Users.Api.Services;

/**
 * Verifies current provider email ownership using only the preconfigured HTTPS identity provider.
 * @param http Bounded client with redirects and cookies disabled.
 * @param configuration Server-owned issuer and authority settings.
 */
public sealed class VerifiedIdentityClient(HttpClient http, IConfiguration configuration)
{
    /**
     * Obtains a verified email bound to the same subject as the already-validated access token.
     * @param issuer Validated issuer.
     * @param subject Validated subject.
     * @param accessToken The user token, sent only to its trusted issuer's userinfo endpoint.
     * @param cancellationToken Request cancellation.
     * @return Verified email, or null when the provider identity cannot be established.
     */
    public async Task<string?> EmailAsync(string issuer, string subject, string accessToken, CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Bearer:Authority"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var provider) || provider.Scheme != Uri.UriSchemeHttps ||
            provider.UserInfo.Length != 0 || provider.Query.Length != 0 || provider.Fragment.Length != 0 ||
            !string.Equals(issuer.TrimEnd('/'), provider.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)) return null;
        var endpoint = new Uri(provider.AbsoluteUri.TrimEnd('/') + "/userinfo", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.String || sub.GetString() != subject ||
            !root.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(email.GetString())) return null;
        return email.GetString();
    }
}
