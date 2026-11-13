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
     * @param accessToken The user token, sent only to the trusted provider's user verification endpoint.
     * @param cancellationToken Request cancellation.
     * @return Verified email, or null when the provider identity cannot be established.
     */
    public async Task<string?> EmailAsync(string issuer, string subject, string accessToken, CancellationToken cancellationToken)
    {
        var authority = configuration["Authentication:Bearer:Authority"];
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var provider) || provider.Scheme != Uri.UriSchemeHttps ||
            provider.UserInfo.Length != 0 || provider.Query.Length != 0 || provider.Fragment.Length != 0 ||
            !string.Equals(issuer.TrimEnd('/'), provider.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal)) return null;
        var supabase = configuration["Authentication:Bearer:Provider"] == "Supabase";
        var key = configuration["Supabase:PublishableKey"];
        if (supabase && (string.IsNullOrWhiteSpace(key) || !provider.AbsolutePath.TrimEnd('/').EndsWith("/auth/v1", StringComparison.Ordinal)))
            return null;
        var endpoint = new Uri(provider.AbsoluteUri.TrimEnd('/') + (supabase ? "/user" : "/userinfo"), UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (supabase)
            request.Headers.Add("apikey", key);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        if (supabase)
        {
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
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("sub", out var sub) || sub.ValueKind != JsonValueKind.String || sub.GetString() != subject ||
            !root.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(email.GetString())) return null;
        return email.GetString();
    }
}
