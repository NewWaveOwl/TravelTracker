using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises production circuit/session state without replacing it with a fake authentication provider. */
public sealed class SessionTests
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    readonly SessionRegistry sessions;
    const string Path = "/api/v1/transport-modes/00000000-0000-0000-0000-000000000101";

    public SessionTests() => sessions = new(clock);

    /**
     * Registers a test identity in the same active-session registry used by production.
     *
     * @param token The token marker to send to the stub service.
     * @return A registered principal valid for five minutes.
     */
    ClaimsPrincipal SignIn(string token = "token") => new(new ClaimsIdentity(
    [
        new Claim(AccessTokenClaims.Token, token),
        new Claim(AccessTokenClaims.ExpiresAt, clock.GetUtcNow().AddMinutes(5).ToUnixTimeSeconds().ToString()),
        new Claim(SessionRegistry.SessionIdClaim, sessions.Create(clock.GetUtcNow().AddMinutes(5))),
    ], "test"));

    /**
     * Supplies a principal through the actual host-facing interface used at circuit start.
     *
     * @param user The cookie principal being restored.
     * @return A production provider owned and disposed by the test.
     */
    SessionAuthenticationStateProvider Circuit(ClaimsPrincipal user)
    {
        var provider = new SessionAuthenticationStateProvider(sessions);
        ((IHostEnvironmentAuthenticationStateProvider)provider).SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        return provider;
    }

    ProtectedClient Client(AuthenticationStateProvider provider, StubHandler handler) => new(provider,
        new ServiceDirectory(new Dictionary<string, string> { ["Transport"] = "http://localhost:5105" }),
        new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
        NullLogger<ProtectedClient>.Instance, sessions);

    [Fact]
    public async Task RevocationNotifiesOtherCircuitsButDoesNotSignOutAnotherSession()
    {
        var user = SignIn();
        using var first = Circuit(user);
        using var second = Circuit(user);
        using var unrelated = Circuit(SignIn("another-token"));
        var notification = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        second.AuthenticationStateChanged += state => notification.TrySetResult(state.GetAwaiter().GetResult());

        sessions.Revoke(SessionRegistry.IdFor(user));

        Assert.False((await notification.Task.WaitAsync(TimeSpan.FromSeconds(10))).User.Identity!.IsAuthenticated);
        Assert.False((await first.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.True((await unrelated.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        var handler = StubHandler.Status(200, "{}");
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => Client(second, handler).GetJsonAsync<object>("Transport", Path))).Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ARevokedPrincipalCannotBeRestoredIntoANewCircuit()
    {
        var user = SignIn();
        sessions.Revoke(SessionRegistry.IdFor(user));

        using var restored = Circuit(user);

        Assert.False((await restored.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task AnUnknownSessionAfterRestartIsAnonymous()
    {
        var user = SignIn();
        using var restarted = new SessionAuthenticationStateProvider(new SessionRegistry(clock));

        restarted.SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));

        Assert.False((await restarted.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task ExpiryRevokesTheSessionInEveryCircuit()
    {
        var user = SignIn();
        using var first = Circuit(user);
        using var second = Circuit(user);
        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.False((await first.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False((await second.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False(sessions.IsActive(user));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    public async Task ALateResponseFromAnOldSessionCannotAffectANewSignIn(int status)
    {
        var old = SignIn("old-token");
        using var circuit = Circuit(old);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler((_, _) => { entered.TrySetResult(); return completion.Task; });
        var request = Client(circuit, handler).GetJsonAsync<object>("Transport", Path);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var replacement = SignIn("replacement-token");
        circuit.SetAuthenticationState(Task.FromResult(new AuthenticationState(replacement)));

        completion.SetResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{}") });

        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => request)).Status);
        Assert.True(sessions.IsActive(replacement));
        Assert.Equal("replacement-token", AccessTokenClaims.ActiveToken((await circuit.GetAuthenticationStateAsync()).User, clock.GetUtcNow()));
    }

    [Fact]
    public async Task AForbiddenResponseKeepsTheSharedSessionActive()
    {
        var user = SignIn();
        using var first = Circuit(user);
        using var second = Circuit(user);

        Assert.Equal(403, (await Assert.ThrowsAsync<ApiError>(() => Client(first, StubHandler.Status(403)).GetJsonAsync<object>("Transport", Path))).Status);

        Assert.True((await second.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.True(sessions.IsActive(user));
    }

    [Fact]
    public async Task ARejectedTokenRevokesEveryCircuitAndBlocksLaterCalls()
    {
        var user = SignIn();
        using var first = Circuit(user);
        using var second = Circuit(user);

        await Assert.ThrowsAsync<ApiError>(() => Client(first, StubHandler.Status(401)).GetJsonAsync<object>("Transport", Path));

        Assert.False((await second.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        var handler = StubHandler.Status(200, "{}");
        await Assert.ThrowsAsync<ApiError>(() => Client(second, handler).GetJsonAsync<object>("Transport", Path));
        Assert.Null(handler.LastRequest);
    }
}
