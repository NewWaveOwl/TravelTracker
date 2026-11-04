using System.Collections.Concurrent;
using System.Security.Claims;

namespace TravelTracker.Frontend.Auth;

/**
 * Tracks active sessions for this frontend process without storing access tokens.
 * Unknown sessions fail closed, including cookies issued before a process restart.
 *
 * @param clock Supplies the current time for expiry and cleanup.
 */
public sealed class SessionRegistry(TimeProvider clock)
{
    public const string SessionIdClaim = "traveltracker:session_id";

    readonly ConcurrentDictionary<string, DateTimeOffset> active = new();

    /** Notifies circuits when a particular session is revoked. */
    public event Action<string>? Revoked;

    /**
     * Issues an unguessable session ID and removes expired registrations.
     *
     * @param expiresAt The access token's expiry; registrations never outlive it.
     * @return A new session ID, active only while its expiry is in the future.
     */
    public string Create(DateTimeOffset expiresAt)
    {
        var now = clock.GetUtcNow();
        foreach (var session in active)
            if (session.Value <= now)
                active.TryRemove(session.Key, out _);
        var id = Guid.NewGuid().ToString("N");
        if (expiresAt > now)
            active[id] = expiresAt;
        return id;
    }

    /**
     * Reads the one server-owned session identifier from an authenticated principal.
     *
     * @param user The cookie or circuit principal.
     * @return Its session ID, or null for an anonymous or ambiguous principal.
     */
    public static string? IdFor(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
            return null;
        var claims = user.FindAll(SessionIdClaim).Take(2).ToArray();
        return claims.Length == 1 ? claims[0].Value : null;
    }

    /**
     * Checks that a cookie or circuit still belongs to an active local session.
     *
     * @param user The cookie or circuit principal.
     * @return True only for a registered, unexpired session with a live access token.
     */
    public bool IsActive(ClaimsPrincipal user)
    {
        var id = IdFor(user);
        if (id is null || !active.TryGetValue(id, out var expiresAt))
            return false;
        var now = clock.GetUtcNow();
        if (expiresAt > now && AccessTokenClaims.ActiveToken(user, now) is not null)
            return true;
        Revoke(id);
        return false;
    }

    /**
     * Invalidates one session for all its cookies and circuits.
     *
     * @param id The captured session ID; null and already-revoked IDs are harmless.
     */
    public void Revoke(string? id)
    {
        if (id is not null && active.TryRemove(id, out _))
            Revoked?.Invoke(id);
    }
}
