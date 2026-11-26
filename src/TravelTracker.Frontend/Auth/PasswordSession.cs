using System.Security.Claims;

namespace TravelTracker.Frontend.Auth;

/**
 * A sign-in the Users API accepted, on its way into the local cookie session.
 * @param User The signed-in principal.
 * @param AccessToken The access token kept inside the encrypted HttpOnly session cookie.
 * @param ExpiresAt When the access token, and so the session, ends.
 */
public sealed record PasswordSession(ClaimsPrincipal User, string AccessToken, DateTimeOffset ExpiresAt);
