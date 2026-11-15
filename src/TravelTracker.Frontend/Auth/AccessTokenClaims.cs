using System.Security.Claims;

namespace TravelTracker.Frontend.Auth;

/** The access token travels in the encrypted, HttpOnly session cookie as claims, so interactive circuits can read it without an HTTP context. */
public static class AccessTokenClaims
{
    /** claim holding the access token itself; never log its value. */
    public const string Token = "traveltracker:access_token";
    /** claim holding the access token expiry as Unix seconds. */
    public const string ExpiresAt = "traveltracker:access_token_expires_at";
    /** claim holding the scopes the provider granted. */
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

    /**
     * Tells whether the user has an authenticated, registered session with a token that has not expired.
     *
     * @param user The current principal.
     * @param sessions Confirms the session has not been revoked or timed out.
     * @param now The current time, passed in so expiry is testable.
     * @return True when the page may treat the user as signed in.
     */
    public static bool IsSignedIn(ClaimsPrincipal user, SessionRegistry sessions, DateTimeOffset now) =>
        user.Identity?.IsAuthenticated == true && sessions.IsActive(user) && ActiveToken(user, now) is not null;

    /**
     * Lists the scopes the provider granted to this session. Only the server-owned grant is trusted for UI hints.
     *
     * @param user The current principal.
     * @return The granted scope names, compared case-sensitively.
     */
    public static HashSet<string> GrantedScopes(ClaimsPrincipal user) => user.FindAll(GrantedScope)
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet(StringComparer.Ordinal);
}
