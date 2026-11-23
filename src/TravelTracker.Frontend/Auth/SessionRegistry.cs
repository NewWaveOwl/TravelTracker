using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;

namespace TravelTracker.Frontend.Auth;

/**
 * Tracks active sessions for this frontend process without storing access tokens.
 * Unknown sessions fail closed, including cookies issued before a process restart.
 *
 * @param clock Supplies the current time for expiry and cleanup.
 * @param logger Records revocation handlers that throw; defaults to a null logger for callers without DI.
 */
public sealed class SessionRegistry(TimeProvider clock, ILogger<SessionRegistry>? logger = null)
{
    public const string SessionIdClaim = "traveltracker:session_id";

    readonly ConcurrentDictionary<string, DateTimeOffset> active = new();
    readonly ILogger<SessionRegistry> handlerLogger = logger ?? NullLogger<SessionRegistry>.Instance;
    readonly object subscriberGate = new();
    Action<string>? revoked;

    /**
     * Notifies circuits when a particular session is revoked; subscriptions may change from any thread.
     */
    public event Action<string>? Revoked
    {
        add { lock (subscriberGate) revoked += value; }
        remove { lock (subscriberGate) revoked -= value; }
    }

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
        var identifier = Guid.NewGuid().ToString("N");
        if (expiresAt > now)
            active[identifier] = expiresAt;
        return identifier;
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
        var identifier = IdFor(user);
        if (identifier is null || !active.TryGetValue(identifier, out var expiresAt))
            return false;
        var now = clock.GetUtcNow();
        if (expiresAt > now && AccessTokenClaims.ActiveToken(user, now) is not null)
            return true;
        Revoke(identifier);
        return false;
    }

    /**
     * Invalidates one session for all its cookies and circuits; a throwing handler is logged and does not stop the others.
     *
     * @param identifier The captured session ID; null and already-revoked IDs are harmless.
     */
    public void Revoke(string? identifier)
    {
        if (identifier is null || !active.TryRemove(identifier, out _))
            return;
        Action<string>? handlers;
        lock (subscriberGate) handlers = revoked;
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<string>>())
        {
            try
            {
                handler(identifier);
            }
            catch (Exception exception)
            {
                handlerLogger.LogError("A session revocation handler failed; the remaining handlers still run ({FailureType})", exception.GetType().Name);
            }
        }
    }
}
