using System.Net;
using System.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Drives hosted sign-in, the session and sign-out through the real middleware with a fake Auth0 tenant. */
public sealed class SignInFlowTests
{
    static async Task<string> AccountHtmlAsync(HttpClient browser) => await (await browser.GetAsync("/account")).Content.ReadAsStringAsync();

    [Fact]
    public async Task SignInRedirectsToAuth0WithPkceAudienceAndScope()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();

        var authorize = await factory.StartSignInAsync(browser);
        var query = HttpUtility.ParseQueryString(authorize.Query);

        Assert.Equal("tenant.example.test", authorize.Host);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("traveltracker-api", query["audience"]);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Contains("catalogue.read", query["scope"]);
        Assert.Contains("users.read", query["scope"]);
        Assert.False(string.IsNullOrEmpty(query["nonce"]));
    }

    [Fact]
    public async Task CompletedSignInStartsASessionThatTheAccountPageShows()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));

        await factory.StartSignInAsync(browser);
        var callback = await factory.CompleteSignInAsync(browser);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account", callback.Headers.Location!.OriginalString);
        Assert.Contains("Profile details", await AccountHtmlAsync(browser));
        Assert.Contains("traveller_1", await AccountHtmlAsync(browser));
        Assert.Equal("authorization_code", Assert.Single(factory.Auth0.TokenRequests)["grant_type"]);
    }

    [Fact]
    public async Task AnExpiredAccessTokenEndsTheSessionImmediately()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.ExpiresInSeconds = 0;
        var browser = factory.CreateBrowser();

        await factory.StartSignInAsync(browser);
        await factory.CompleteSignInAsync(browser);

        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task AnIdTokenWithTheWrongNonceDoesNotSignIn()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.IdTokenNonceOverride = "someone-elses-nonce";
        var browser = factory.CreateBrowser();

        await factory.StartSignInAsync(browser);
        var callback = await factory.CompleteSignInAsync(browser);

        Assert.StartsWith("/account?signin=", callback.Headers.Location!.OriginalString);
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task ACallbackWithoutTheCorrelationCookieDoesNotSignIn()
    {
        using var factory = new FrontendFactory();
        var starter = factory.CreateBrowser();
        await factory.StartSignInAsync(starter);

        var stranger = factory.CreateBrowser();
        var callback = await factory.CompleteSignInAsync(stranger);

        Assert.StartsWith("/account?signin=", callback.Headers.Location!.OriginalString);
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(stranger));
    }

    [Fact]
    public async Task ARejectedPkceVerifierDoesNotSignIn()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.StartSignInAsync(browser);
        factory.Auth0.CodeChallenge = "not-the-challenge";

        var callback = await factory.CompleteSignInAsync(browser);

        Assert.StartsWith("/account?signin=", callback.Headers.Location!.OriginalString);
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task ADeniedGrantShowsOnlyTheFixedMessage()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.StartSignInAsync(browser);
        const string description = "Client \"secret-client-name\" is not authorized to access resource server \"traveltracker-api\".";

        var callback = await browser.GetAsync($"/signin-oidc?error=access_denied&error_description={Uri.EscapeDataString(description)}&state={Uri.EscapeDataString(factory.State!)}");
        var location = callback.Headers.Location!.OriginalString;
        var page = await (await browser.GetAsync(location)).Content.ReadAsStringAsync();

        Assert.Equal($"/account?signin={LoginFailure.NotAuthorized}", location);
        Assert.Contains("Ask the administrator to authorize its access", page);
        Assert.DoesNotContain("secret-client-name", page);
        Assert.Contains("Sign in to view your profile", page);
    }

    [Fact]
    public async Task SignOutClearsTheSessionAndEndsTheAuth0Session()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.StartSignInAsync(browser);
        await factory.CompleteSignInAsync(browser);
        var token = FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(browser));

        var logout = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", token)]));

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var destination = logout.Headers.Location!;
        Assert.Equal("tenant.example.test", destination.Host);
        Assert.Equal("/v2/logout", destination.AbsolutePath);
        Assert.Equal("test-client", HttpUtility.ParseQueryString(destination.Query)["client_id"]);
        Assert.Contains("Sign in to view your profile", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task SignOutWithoutAnAntiforgeryTokenIsRefusedAndKeepsTheSession()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.StartSignInAsync(browser);
        await factory.CompleteSignInAsync(browser);

        var logout = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Contains("Profile details", await AccountHtmlAsync(browser));
    }

    [Fact]
    public async Task WithoutAuth0SettingsTheAppServesASetupPendingAccountPage()
    {
        using var factory = new FrontendFactory(signInConfigured: false);
        var browser = factory.CreateBrowser();

        var login = await browser.GetAsync("/auth/login");
        var page = await AccountHtmlAsync(browser);

        Assert.Equal(HttpStatusCode.NotFound, login.StatusCode);
        Assert.Contains("temporarily unavailable", page);
    }

    static async Task<HttpResponseMessage> SignInAsync(FrontendFactory factory, HttpClient browser)
    {
        await factory.StartSignInAsync(browser);
        return await factory.CompleteSignInAsync(browser);
    }

    static async Task<bool> SignedInAsync(HttpClient browser)
    {
        var page = await AccountHtmlAsync(browser);
        return page.Contains("Profile details") && page.Contains("traveller_1");
    }

    // what live Auth0 can send that the handler must refuse

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    public async Task ARejectedIdTokenNeverStartsASession(string defect)
    {
        using var factory = new FrontendFactory();
        switch (defect)
        {
            case "issuer": factory.Auth0.IdTokenIssuer = "https://other-tenant.example.test/"; break;
            case "audience": factory.Auth0.IdTokenAudience = "another-client"; break;
            case "signature": factory.Auth0.SignWithUnpublishedKey = true; break;
            default: factory.Auth0.IdTokenLifetime = TimeSpan.FromMinutes(-5); break;
        }
        var browser = factory.CreateBrowser();

        var callback = await SignInAsync(factory, browser);

        Assert.StartsWith("/account?signin=", callback.Headers.Location!.OriginalString);
        Assert.False(await SignedInAsync(browser));
    }

    [Fact]
    public async Task ACallbackCannotBeReplayed()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);
        await browser.PostAsync("/auth/logout", new FormUrlEncodedContent([new("__RequestVerificationToken", FrontendFactory.AntiforgeryTokenFrom(await AccountHtmlAsync(browser)))]));

        var replay = await factory.CompleteSignInAsync(browser);

        Assert.StartsWith("/account?signin=", replay.Headers.Location!.OriginalString);
        Assert.False(await SignedInAsync(browser));
        Assert.Single(factory.Auth0.TokenRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ATokenEndpointFailureShowsOnlyAFixedMessage(HttpStatusCode status)
    {
        using var factory = new FrontendFactory();
        factory.Auth0.TokenEndpointFailure = status;
        var browser = factory.CreateBrowser();

        var callback = await SignInAsync(factory, browser);
        var page = await (await browser.GetAsync(callback.Headers.Location!.OriginalString)).Content.ReadAsStringAsync();

        Assert.Contains("Unable to sign in", page);
        Assert.DoesNotContain("tenant-internal-detail", page);
        Assert.False(await SignedInAsync(browser));
    }

    [Theory]
    [InlineData("login_required", LoginFailure.LoginRequired)]
    [InlineData("consent_required", LoginFailure.ConsentRequired)]
    [InlineData("access_denied", LoginFailure.Canceled)]
    public async Task ProviderErrorCodesMapToTheirFixedFailureCodes(string error, string expectedCode)
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();
        await factory.StartSignInAsync(browser);

        var callback = await browser.GetAsync($"/signin-oidc?error={error}&state={Uri.EscapeDataString(factory.State!)}");

        Assert.Equal($"/account?signin={expectedCode}", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ATokenResponseWithoutAnAccessTokenDoesNotCountAsSignedIn()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.OmitAccessToken = true;
        var browser = factory.CreateBrowser();

        await SignInAsync(factory, browser);

        Assert.False(await SignedInAsync(browser));
    }

    [Fact]
    public async Task TheAuthorizeRequestUsesTheRegisteredCallbackAndIgnoresAReturnUrl()
    {
        using var factory = new FrontendFactory();
        var browser = factory.CreateBrowser();

        var login = await browser.GetAsync("/auth/login?returnUrl=https://evil.example/steal");
        var query = HttpUtility.ParseQueryString(login.Headers.Location!.Query);
        factory.Auth0.Nonce = query["nonce"];
        factory.Auth0.CodeChallenge = query["code_challenge"];
        var callback = await browser.GetAsync($"/signin-oidc?code=fake-code&state={Uri.EscapeDataString(query["state"]!)}");

        Assert.Equal("http://localhost/signin-oidc", query["redirect_uri"]);
        Assert.Equal("/account", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ThePublicClientExchangesTheCodeWithPkceAndNoSecret()
    {
        using var factory = new FrontendFactory();

        await SignInAsync(factory, factory.CreateBrowser());

        var request = Assert.Single(factory.Auth0.TokenRequests);
        Assert.Equal("fake-code", request["code"]);
        Assert.Equal("http://localhost/signin-oidc", request["redirect_uri"]);
        Assert.False(string.IsNullOrEmpty(request["code_verifier"]));
        Assert.False(request.ContainsKey("client_secret"));
    }

    [Fact]
    public async Task TheRegularWebApplicationSendsItsSecretOnlyToTheTokenEndpoint()
    {
        using var factory = new FrontendFactory(clientSecret: "s3cret-value");
        var browser = factory.CreateBrowser();

        var login = await browser.GetAsync("/auth/login");
        await SignInAsync(factory, factory.CreateBrowser());

        Assert.DoesNotContain("s3cret-value", login.Headers.Location!.OriginalString);
        Assert.Equal("s3cret-value", Assert.Single(factory.Auth0.TokenRequests)["client_secret"]);
    }

    // what the session leaves in the browser

    [Fact]
    public async Task TheAccessTokenNeverReachesThePageAndTheCookieIsHardened()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.AccessToken = "very-recognisable-access-token";
        var browser = factory.CreateBrowser();

        var callback = await SignInAsync(factory, browser);
        var page = await AccountHtmlAsync(browser);
        var session = callback.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Cookies"));

        Assert.True(await SignedInAsync(browser));
        Assert.DoesNotContain("very-recognisable-access-token", page);
        Assert.DoesNotContain("very-recognisable-access-token", session);
        Assert.Contains("httponly", session, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", session, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", session, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheSessionEndsWhenTheAccessTokenExpires()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.ExpiresInSeconds = 3600;
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
        factory.Auth0.AccessToken = "token-for-transport";
        var browser = factory.CreateBrowser();
        await SignInAsync(factory, browser);

        var response = await browser.GetAsync(FrontendFactory.ProtectedReadPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = factory.Transport.LastRequest!;
        Assert.Equal("Bearer token-for-transport", sent.Headers.Authorization!.ToString());
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
    public async Task SignInWithAnUnreachableTenantRedirectsToAFixedMessageInsteadOfAnError()
    {
        using var factory = new FrontendFactory();
        factory.Auth0.Unreachable = true;
        var browser = factory.CreateBrowser();

        var login = await browser.GetAsync("/auth/login");
        var page = await (await browser.GetAsync(login.Headers.Location!.OriginalString)).Content.ReadAsStringAsync();

        Assert.Equal($"/account?signin={LoginFailure.StartFailed}", login.Headers.Location!.OriginalString);
        Assert.Contains("Unable to start sign-in. Please try again.", page);
        Assert.DoesNotContain("tenant unreachable", page);
    }

    [Fact]
    public async Task SignOutEndsTheLocalSessionEvenWhenAuth0LogoutCannotBeStarted()
    {
        var keys = Directory.CreateTempSubdirectory("traveltracker-keys").FullName;
        var sessions = new SessionRegistry(TimeProvider.System);
        try
        {
            // Share sessions here to isolate an uncached discovery outage; a real process restart invalidates sessions.
            string sessionCookie;
            using (var first = new FrontendFactory(keyDirectory: keys, sessionRegistry: sessions))
            {
                var browser = first.CreateBrowser();
                var callback = await SignInAsync(first, browser);
                sessionCookie = callback.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Cookies")).Split(';')[0];
            }
            using var restarted = new FrontendFactory(keyDirectory: keys, sessionRegistry: sessions);
            restarted.Auth0.Unreachable = true;
            var returning = restarted.CreateBrowser();
            HttpRequestMessage WithSession(HttpMethod method, string path)
            {
                var request = new HttpRequestMessage(method, path);
                request.Headers.Add("Cookie", sessionCookie);
                return request;
            }
            var pageResponse = await returning.SendAsync(WithSession(HttpMethod.Get, "/account"));
            var html = await pageResponse.Content.ReadAsStringAsync();
            Assert.Contains("Profile details", html);
            Assert.Contains("traveller_1", html);
            var token = FrontendFactory.AntiforgeryTokenFrom(html);

            var logout = WithSession(HttpMethod.Post, "/auth/logout");
            logout.Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]);
            var response = await returning.SendAsync(logout);

            Assert.Equal("/account?signedout=local", response.Headers.Location!.OriginalString);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(".AspNetCore.Cookies=;"));
            var followUp = await (await returning.GetAsync("/account?signedout=local")).Content.ReadAsStringAsync();
            Assert.Contains("Your TravelTracker session has ended", followUp);
            Assert.Contains("Sign in to view your profile", followUp);
        }
        finally
        {
            Directory.Delete(keys, recursive: true);
        }
    }

    [Fact]
    public void Auth0CallsAreBoundedToTenSeconds()
    {
        using var factory = new FrontendFactory();

        var options = factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions>>().Get("OpenIdConnect");

        Assert.Equal(TimeSpan.FromSeconds(10), options.BackchannelTimeout);
    }

    /**
     * Reads a test cookie using the real cookie handler's data protection format.
     *
     * @param factory The host that issued the encrypted cookie.
     * @param callback The successful OIDC callback response.
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
        var handler = new StubHandler((_, _) => { entered.TrySetResult(); return completion.Task; });
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
        factory.Auth0.AccessToken = "replacement-token";
        factory.Transport.RespondWith(200, FrontendFactory.SelfProfileJson);
        await SignInAsync(factory, browser);
        Assert.True(await SignedInAsync(browser));
        factory.Transport.RespondWith(200, "{}");
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        Assert.Equal("Bearer replacement-token", factory.Transport.LastRequest!.Headers.Authorization!.ToString());
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
        var handler = new StubHandler((_, _) => { entered.TrySetResult(); return completion.Task; });
        var client = new ProtectedClient(oldCircuit, factory.Services.GetRequiredService<ServiceDirectory>(),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), factory.Clock,
            NullLogger<ProtectedClient>.Instance, factory.Services.GetRequiredService<SessionRegistry>());
        var pending = client.GetJsonAsync<object>("Transport", "/api/v1/transport-modes/example");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        factory.Auth0.AccessToken = "replacement-token";
        await SignInAsync(factory, browser);
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => pending)).Status);
        Assert.False((await oldCircuit.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated);
        Assert.True(await SignedInAsync(browser));
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(FrontendFactory.ProtectedReadPath)).StatusCode);
        Assert.Equal("Bearer replacement-token", factory.Transport.LastRequest!.Headers.Authorization!.ToString());
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
