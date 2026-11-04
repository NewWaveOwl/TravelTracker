using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Tests.Support;

/** A test authentication state whose user can be signed in with a token and signed out again. */
public sealed class SwitchableAuthentication : AuthenticationStateProvider
{
    public ClaimsPrincipal User { get; private set; } = new(new ClaimsIdentity());

    public void SignIn(string token, DateTimeOffset expiresAt) => User = new ClaimsPrincipal(new ClaimsIdentity(
    [
        new Claim(AccessTokenClaims.Token, token),
        new Claim(AccessTokenClaims.ExpiresAt, expiresAt.ToUnixTimeSeconds().ToString()),
    ], "test"));

    public void SignOut() => User = new ClaimsPrincipal(new ClaimsIdentity());

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
}
