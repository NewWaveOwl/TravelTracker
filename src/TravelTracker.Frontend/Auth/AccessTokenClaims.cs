using System.Security.Claims;

namespace TravelTracker.Frontend.Auth;

/** The access token travels in the encrypted, HttpOnly session cookie as claims, so interactive circuits can read it without an HTTP context. */
public static class AccessTokenClaims
{
    public const string Token = "traveltracker:access_token";
    public const string ExpiresAt = "traveltracker:access_token_expires_at";
    public const string GrantedScope = "traveltracker:granted_scope";

    /**
     * Reads the access token from a signed-in user.
     *
     * @param user The current principal.
     * @param now The current time, passed in so expiry is testable.
     * @return The token while it is still valid; otherwise null.
     */
    public static string? ActiveToken(ClaimsPrincipal user, DateTimeOffset now)
    {
        var token = user.FindFirstValue(Token);
        if (string.IsNullOrEmpty(token) || !long.TryParse(user.FindFirstValue(ExpiresAt), out var expiresAt))
            return null;
        return DateTimeOffset.FromUnixTimeSeconds(expiresAt) > now ? token : null;
    }
}
