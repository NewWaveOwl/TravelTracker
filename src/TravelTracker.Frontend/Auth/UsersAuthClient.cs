using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelTracker.Frontend.Api;

namespace TravelTracker.Frontend.Auth;

/**
 * Signs travellers in and registers them through the Users API, the only place passwords are checked.
 * The token is read, not signature-checked: it came straight from the configured Users service and every API verifies it again.
 * @param http A bounded client with redirects and cookies disabled.
 * @param services Locates the Users API and refuses insecure destinations.
 * @param clock Supplies the session clock.
 */
public sealed class UsersAuthClient(HttpClient http, ServiceDirectory services, TimeProvider clock)
{
    /**
     * Checks a password.
     * @param username The submitted sign-in username.
     * @param password The submitted password, never logged or persisted.
     * @param cancellationToken Cancels the request.
     * @return A session, or null when the credentials or the account standing were refused. Transport and server failures throw.
     */
    public Task<PasswordSession?> SignInAsync(string username, string password, CancellationToken cancellationToken) =>
        PostAsync("login", username, password, cancellationToken);

    /**
     * Creates an account and signs it in.
     * @param username The new account username.
     * @param password The new account password.
     * @param cancellationToken Cancels the request.
     * @return A session, or null when the username is taken or the input was refused. Transport and server failures throw.
     */
    public Task<PasswordSession?> RegisterAsync(string username, string password, CancellationToken cancellationToken) =>
        PostAsync("register", username, password, cancellationToken);

    private async Task<PasswordSession?> PostAsync(string action, string username, string password, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync(services.ProtectedUrlFor("Users", $"/api/v1/auth/{action}"), new { username, password }, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Conflict) return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TokenBody>(cancellationToken);
        if (body is not { AccessToken.Length: > 0 }) return null;

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.AccessToken);
        var expiresAt = new DateTimeOffset(token.ValidTo, TimeSpan.Zero);
        if (string.IsNullOrWhiteSpace(token.Subject) || expiresAt <= clock.GetUtcNow()) return null;
        var identity = new ClaimsIdentity([new("sub", token.Subject), new("name", username), new("scope", body.Scope ?? "")], "password", "name", null);
        return new(new ClaimsPrincipal(identity), body.AccessToken, expiresAt);
    }

    private sealed record TokenBody(string? AccessToken, string? Scope);
}
