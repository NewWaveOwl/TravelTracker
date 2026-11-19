using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** drives password sign-in, the session and sign-out through the real middleware with a fake Users API. */
public sealed class SignInFlowTests
{
    static async Task<string> AccountHtmlAsync(HttpClient browser) => await (await browser.GetAsync("/account")).Content.ReadAsStringAsync();

    static Task<HttpResponseMessage> SignInAsync(FrontendFactory factory, HttpClient browser) => factory.SignInAsync(browser);

    static async Task<bool> SignedInAsync(HttpClient browser)
    {
        var page = await AccountHtmlAsync(browser);
        return page.Contains("Profile details") && page.Contains("traveller_1");
    }

    /**
     * reads a test cookie using the real cookie handler's data protection format.
     *
     * @param factory The host that issued the encrypted cookie.
     * @param callback The successful login response.
     * @return The authenticated principal and cookie header for controlled restoration tests.
     */
    static (System.Security.Claims.ClaimsPrincipal User, string Cookie) SessionFrom(FrontendFactory factory, HttpResponseMessage callback)
    {
        var cookie = callback.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(".AspNetCore.Cookies=")).Split(';')[0];
        var value = cookie[(cookie.IndexOf('=') + 1)..];
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        return (options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(value))!.Principal, cookie);
    }

    [Fact]
    public async Task CompletedSignInStartsASessionThatTheAccountPageShows()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));

        var callback = await factory.SignInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account", callback.Headers.Location!.OriginalString);
        Assert.Contains("Profile details", await AccountHtmlAsync(browser));
        Assert.Contains("traveller_1", await AccountHtmlAsync(browser));
        Assert.Equal(1, factory.Users.LoginRequests);
    }

    [Fact]
    public async Task AnExpiredAccessTokenEndsTheSessionImmediately()
    {
        using var factory = new FrontendFactory();
        factory.Users.ExpiresInSeconds = 0;
        var browser = factory.CreateBrowser();

        await factory.SignInAsync(browser);

        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task SignOutClearsTheSession()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.SignInAsync(browser);
        var token = FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(browser));

        var logout = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/account", logout.Headers.Location!.OriginalString);
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task SignOutWithoutAnAntiforgeryTokenIsRefusedAndKeepsTheSession()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.SignInAsync(browser);

        var logout = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Contains("Profile details", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task TheAccessTokenNeverReachesThePageAndTheCookieIsHardened()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();

        var callback = await SignInAsync(factory, browser);
        var recognisable = factory.Users.LastToken!;
        var page = await AccountHtmlAsync(browser);
        var session = callback.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Cookies"));

        Assert.True(await SignedInAsync(browser));
        Assert.DoesNotContain(recognisable, page);
        Assert.DoesNotContain(recognisable, session);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", session, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSessionEndsWhenTheAccessTokenExpires()
    {
        using var factory = new FrontendFactory();
        factory.Users.ExpiresInSeconds = 3600;
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);
        factory.Clock.Advance(TimeSpan.FromMinutes(59));
        Assert.True(await SignedInAsync(browser));

        factory.Clock.Advance(TimeSpan.FromMinutes(2));

        Assert.False(await SignedInAsync(browser));
    }

    // the "Check API access" path: cookie to circuit user to bearer request

    [Fact]
    public async Task CheckApiAccessSendsTheIssuedAccessTokenToTransport()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);

        var response = await browser.GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = factory.Transport.LastRequest!;
        Assert.Equal("Bearer " + factory.Users.LastToken, sent.Headers.Authorization!.ToString());
        Assert.Equal("http://localhost:5105/api/v1/transport-modes/00000000-0000-0000-0000-000000000101", sent.RequestUri!.ToString());
    }

    [Fact]
    public async Task CheckApiAccessSendsNothingWhenSignedOut()
    {
        using var factory = new FrontendFactory();

        var response = await factory.CreateBrowser().GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.Transport.LastRequest);
    }

    [Fact]
    public async Task CheckApiAccessSendsNothingOnceTheTokenHasExpired()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);
        factory.Clock.Advance(TimeSpan.FromMinutes(61));

        var response = await browser.GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.Transport.LastRequest);
    }

    [Theory]
    [InlineData(401, HttpStatusCode.Unauthorized)]
    [InlineData(403, HttpStatusCode.Forbidden)]
    public async Task CheckApiAccessSurfacesTransportRejections(int transportStatus, HttpStatusCode expected)
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);
        factory.Transport.RespondWith(transportStatus);

        var response = await browser.GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task CheckApiAccessAfterSignOutSendsNothingNew()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);
        await browser.GetAsync(FrontendFactory.ProtectedReadPath);
        var signOutToken = FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(browser));
        factory.Transport.Forget();
        await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", signOutToken)]));

        var response = await browser.GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.Transport.LastRequest);
    }

    // recoverable failures when the tenant cannot be reached

    [Fact]
    public async Task HttpSignOutRevokesOtherOpenCircuitsAndDropsTheirInflightResponse()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        var callback = await SignInAsync(factory, browser);
        var (user, _) = SessionFrom(factory, callback);
        using var firstScope = factory.Services.CreateScope();
        using var secondScope = factory.Services.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        var second = secondScope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        ((IHostEnvironmentAuthenticationStateProvider)first).SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        ((IHostEnvironmentAuthenticationStateProvider)second).SetAuthenticationState(Task.FromResult(new AuthenticationState(user)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler((_, _) =>
        {
            entered.TrySetResult();
            return completion.Task;
        });
        var client = new ProtectedClient(second, factory.Services.GetRequiredService<ServiceDirectory>(),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), factory.Clock,
            NullLogger<ProtectedClient>.Instance, factory.Services.GetRequiredService<SessionRegistry>());
        var pending = client.GetJsonAsync<object>("Transport", "/api/v1/transport-modes/example");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var token = FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(browser));

        var logout = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.False((await first.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.False((await second.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => pending)).Status);
    }

    [Fact]
    public async Task ARejectedTokenCannotBeRestoredByAnOldCookieAndAFreshSignInWorks()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        var callback = await SignInAsync(factory, browser);
        var (_, oldCookie) = SessionFrom(factory, callback);
        factory.Transport.RespondWith(401);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        factory.Transport.Forget();
        factory.Transport.RespondWith(200);
        Assert.False(await SignedInAsync(browser));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        Assert.Null(factory.Transport.LastRequest);
        using var replay = factory.CreateDefaultClient();
        using var stale = new HttpRequestMessage(HttpMethod.Get, "/account");
        stale.Headers.Add("Cookie", oldCookie);

        var refused = await replay.SendAsync(stale);

        Assert.Contains("Sign in to view your profile", await refused.Content.ReadAsStringAsync());
        Assert.Contains(refused.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Cookies=;"));
        factory.Transport.RespondWith(200, FrontendFactory.SelfProfileJson);
        await SignInAsync(factory, browser);
        Assert.True(await SignedInAsync(browser));
        factory.Transport.RespondWith(200, "{}");
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        Assert.Equal("Bearer " + factory.Users.LastToken, factory.Transport.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ReplacingTheBrowserSessionRevokesOldCircuitsWithoutRevokingTheReplacement()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        var firstCallback = await SignInAsync(factory, browser);
        var (firstUser, _) = SessionFrom(factory, firstCallback);
        using var scope = factory.Services.CreateScope();
        var oldCircuit = scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        ((IHostEnvironmentAuthenticationStateProvider)oldCircuit).SetAuthenticationState(Task.FromResult(new AuthenticationState(firstUser)));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler((_, _) =>
        {
            entered.TrySetResult();
            return completion.Task;
        });
        var client = new ProtectedClient(oldCircuit, factory.Services.GetRequiredService<ServiceDirectory>(),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), factory.Clock,
            NullLogger<ProtectedClient>.Instance, factory.Services.GetRequiredService<SessionRegistry>());
        var pending = client.GetJsonAsync<object>("Transport", "/api/v1/transport-modes/example");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await SignInAsync(factory, browser);
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => pending)).Status);
        Assert.False((await oldCircuit.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.True(await SignedInAsync(browser));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        Assert.Equal("Bearer " + factory.Users.LastToken, factory.Transport.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task RevokingOneBrowserSessionDoesNotAffectAnotherSignedInBrowser()
    {
        using var factory = new FrontendFactory();
        var first = factory.CreateBrowser();
        await SignInAsync(factory, first);
        var second = factory.CreateBrowser();
        await SignInAsync(factory, second);
        var token = FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(first));

        await first.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.False(await SignedInAsync(first));
        Assert.True(await SignedInAsync(second));
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
    }

    [Fact]
    public async Task AProcessRestartRejectsTheOldCookieEvenWithTheSameEncryptionKeys()
    {
        var keys = Directory.CreateTempSubdirectory("traveltracker-restart").FullName;
        try
        {
            string oldCookie;
            using (var first = new FrontendFactory(keyDirectory: keys))
                oldCookie = SessionFrom(first, await SignInAsync(first, first.CreateBrowser())).Cookie;
            using var restarted = new FrontendFactory(keyDirectory: keys);
            var browser = restarted.CreateBrowser();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
            request.Headers.Add("Cookie", oldCookie);

            var response = await browser.SendAsync(request);

            Assert.Contains("Sign in to view your profile", await response.Content.ReadAsStringAsync());
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Cookies=;"));
        }
        finally
        {
            Directory.Delete(keys, recursive: true);
        }
    }
}
