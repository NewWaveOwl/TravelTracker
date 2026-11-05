using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Services;

/**
 * Shares only a navigation hint from live Users authority, never an authorization decision for writes.
 * @param users Reads the service-owned current profile.
 * @param authentication Supplies the validated, revocable frontend session.
 * @param sessions Verifies that session is active.
 * @param clock Checks access-token expiry.
 */
public sealed class AdminAccessState(UsersClient users, AuthenticationStateProvider authentication, SessionRegistry sessions, TimeProvider clock) : IDisposable
{
    int version;
    int refreshVersion;
    bool subscribed;
    bool disposed;
    public bool IsAdministrator { get; private set; }
    public event Action? Changed;

    /**
     * Rechecks the server-owned role and standing for the current session.
     * @param cancellationToken Cancels a closed navigation or panel request.
     * @return The live profile, or null for an inactive or superseded session.
     */
    public async Task<SelfUserProfile?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!subscribed) { authentication.AuthenticationStateChanged += AuthenticationChanged; subscribed = true; }
        var currentVersion = version;
        var currentRefresh = ++refreshVersion;
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        if (disposed || currentVersion != version) return null;
        if (user.Identity?.IsAuthenticated != true || !sessions.IsActive(user) || AccessTokenClaims.ActiveToken(user, clock.GetUtcNow()) is null)
        {
            Clear();
            return null;
        }
        try
        {
            var profile = await users.GetMeAsync(cancellationToken);
            if (disposed || currentVersion != version) return null;
            if (currentRefresh == refreshVersion)
            {
                IsAdministrator = profile is { Role: "administrator", Status: "active" };
                Changed?.Invoke();
            }
            return profile;
        }
        catch (ApiError) { if (!disposed && currentVersion == version && currentRefresh == refreshVersion) Clear(); throw; }
    }

    /** Clears the display hint immediately; the next navigation or panel refresh asks the service again. */
    public void Clear() { refreshVersion++; IsAdministrator = false; Changed?.Invoke(); }

    void AuthenticationChanged(Task<AuthenticationState> _) { version++; Clear(); }

    /** Removes the session notification subscription when the circuit closes. */
    public void Dispose() { disposed = true; version++; if (subscribed) authentication.AuthenticationStateChanged -= AuthenticationChanged; }
}
