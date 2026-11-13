using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace TravelTracker.Frontend.Auth;

/**
 * Talks only to the configured Supabase Auth origin and validates asymmetric access tokens.
 * @param http A bounded client with redirects, cookies and HTTP logging disabled.
 * @param settings Public project settings.
 * @param clock Supplies the session clock.
 */
public sealed class SupabaseAuthClient(HttpClient http, SupabaseSettings settings, TimeProvider clock)
{
    /**
     * Signs in and verifies the token signature, claims and current verified email owner.
     * @param email The submitted sign-in email.
     * @param password The submitted password, never logged or persisted.
     * @param cancellationToken Cancels all provider requests.
     * @return A validated session, or null when credentials or provider identity are rejected.
     */
    public async Task<SupabaseSession?> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/token?grant_type=password", new { email, password }, null, cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("access_token", out var value) || value.ValueKind != JsonValueKind.String ||
            value.GetString() is not { Length: > 0 } token) return null;

        using var keysResponse = await SendAsync(HttpMethod.Get, "/.well-known/jwks.json", null, null, cancellationToken);
        if (!keysResponse.IsSuccessStatusCode) return null;
        var keys = new JsonWebKeySet(await keysResponse.Content.ReadAsStringAsync(cancellationToken)).GetSigningKeys();
        var validation = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true, RequireSignedTokens = true, IssuerSigningKeys = keys,
            ValidateIssuer = true, ValidIssuer = settings.Issuer,
            ValidateAudience = true, ValidAudience = "authenticated",
            ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = ["ES256", "RS256"], NameClaimType = "email",
            LifetimeValidator = (notBefore, expires, _, _) => expires is not null &&
                new DateTimeOffset(expires.Value, TimeSpan.Zero) > clock.GetUtcNow() &&
                (notBefore is null || new DateTimeOffset(notBefore.Value, TimeSpan.Zero) <= clock.GetUtcNow())
        });
        if (!validation.IsValid || validation.ClaimsIdentity is not { } identity) return null;
        var principal = new ClaimsPrincipal(identity);
        var subjects = principal.FindAll("sub").ToArray();
        var roles = principal.FindAll("role").ToArray();
        if (subjects.Length != 1 || !Guid.TryParse(subjects[0].Value, out var subject) || subject == Guid.Empty ||
            roles.Length != 1 || roles[0].Value != "authenticated" ||
            principal.FindAll("is_anonymous").Any(claim => !string.Equals(claim.Value, "false", StringComparison.OrdinalIgnoreCase)) ||
            principal.FindAll("scope").Any(claim => claim.Value.Split(' ').Contains("users.access", StringComparer.Ordinal))) return null;

        using var userResponse = await SendAsync(HttpMethod.Get, "/user", null, token, cancellationToken);
        if (!userResponse.IsSuccessStatusCode) return null;
        using var userBody = await JsonDocument.ParseAsync(await userResponse.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var user = userBody.RootElement;
        if (!user.TryGetProperty("id", out var id) || id.GetString() != subjects[0].Value ||
            !user.TryGetProperty("email_confirmed_at", out var confirmed) || confirmed.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(confirmed.GetString(), out _) ||
            user.TryGetProperty("is_anonymous", out var anonymous) && anonymous.ValueKind == JsonValueKind.True) return null;
        var expiry = new DateTimeOffset(validation.SecurityToken.ValidTo, TimeSpan.Zero);
        // Only the signed scope claim controls API access; user_metadata can never grant permissions.
        return new(principal, token, expiry);
    }

    /**
     * Requests account registration; the caller never treats this as a verified sign-in.
     * @param email New account email.
     * @param password New account password.
     * @param cancellationToken Cancels the provider request.
     * @return Whether the provider accepted the request without revealing account existence.
     */
    public async Task<bool> SignUpAsync(string email, string password, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/signup", new { email, password }, null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /**
     * Verifies a confirmation email hash without retaining the provider's returned session tokens.
     * @param tokenHash The one-time hash from Supabase's confirmation email template.
     * @param cancellationToken Cancels the provider request.
     * @return Whether confirmation succeeded.
     */
    public async Task<bool> ConfirmEmailAsync(string tokenHash, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/verify", new { token_hash = tokenHash, type = "email" }, null, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /**
     * Revokes the provider's current session after local revocation has already completed.
     * @param token The session's access token.
     * @param cancellationToken Cancels the provider request.
     * @return Whether provider logout succeeded or the token was already invalid.
     */
    public async Task<bool> SignOutAsync(string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Post, "/logout?scope=local", null, token, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
    }

    /**
     * Sends a provider request without following redirects to an untrusted destination.
     * @param method The HTTP method.
     * @param path A fixed Auth API path owned by this integration.
     * @param body Optional request body.
     * @param token Optional validated session token.
     * @param cancellationToken Request cancellation.
     * @return The caller-owned provider response.
     */
    async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, settings.Issuer + path);
        request.Headers.Add("apikey", settings.PublishableKey);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, cancellationToken);
    }
}
