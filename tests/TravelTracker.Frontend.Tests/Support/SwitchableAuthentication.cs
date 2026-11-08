using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Tests.Support;

/** A test authentication state whose user can be signed in with a token and signed out again. */
public sealed class SwitchableAuthentication(SessionRegistry sessions) : AuthenticationStateProvider
{
    public SessionRegistry Sessions { get; } = sessions;
    public ClaimsPrincipal User { get; private set; } = new(new ClaimsIdentity());

    public void SignIn(string token, DateTimeOffset expiresAt, string scopes = "users.read users.write") => User = new ClaimsPrincipal(new ClaimsIdentity(
    [
        new Claim(AccessTokenClaims.Token, token),
        new Claim(AccessTokenClaims.ExpiresAt, expiresAt.ToUnixTimeSeconds().ToString()),
        new Claim(AccessTokenClaims.GrantedScope, scopes),
        new Claim(SessionRegistry.SessionIdClaim, Sessions.Create(expiresAt)),
    ], "test"));

    public void SignOut()
    {
        Sessions.Revoke(SessionRegistry.IdFor(User));
        User = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
}
