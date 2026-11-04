using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace TravelTracker.Frontend.Auth;

/**
 * Connects each circuit's authentication state to the shared revocable session.
 *
 * @param sessions The frontend process's active sessions.
 */
public sealed class SessionAuthenticationStateProvider : AuthenticationStateProvider, IHostEnvironmentAuthenticationStateProvider, IDisposable
{
    static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    readonly SessionRegistry sessions;
    Task<AuthenticationState> state = Task.FromResult(Anonymous);

    public SessionAuthenticationStateProvider(SessionRegistry sessions)
    {
        this.sessions = sessions;
        sessions.Revoked += OnRevoked;
    }

    /**
     * Returns the current host principal only while its shared session remains active.
     *
     * @return The current valid state, or an anonymous state after revocation or expiry.
     */
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var pending = Volatile.Read(ref state);
        var current = await pending;
        if (!ReferenceEquals(pending, Volatile.Read(ref state)))
            return await GetAuthenticationStateAsync();
        return sessions.IsActive(current.User) ? current : Anonymous;
    }

    /**
     * Accepts the host's initial or replacement principal without trusting a revoked cookie.
     *
     * @param authenticationStateTask The state restored by the Blazor host.
     */
    public void SetAuthenticationState(Task<AuthenticationState> authenticationStateTask)
    {
        ArgumentNullException.ThrowIfNull(authenticationStateTask);
        Interlocked.Exchange(ref state, authenticationStateTask);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /** Removes this circuit's subscription when its scope ends. */
    public void Dispose() => sessions.Revoked -= OnRevoked;

    void OnRevoked(string id)
    {
        var current = Volatile.Read(ref state);
        if (current.IsCompletedSuccessfully && SessionRegistry.IdFor(current.Result.User) == id)
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
