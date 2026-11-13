using System.Security.Claims;

namespace TravelTracker.Frontend.Auth;

/**
 * Carries only a cryptographically validated provider session into local cookie creation.
 * @param User The validated user principal.
 * @param AccessToken The access token kept inside the encrypted HttpOnly session cookie.
 * @param ExpiresAt The provider token expiry.
 */
public sealed record SupabaseSession(ClaimsPrincipal User, string AccessToken, DateTimeOffset ExpiresAt);
