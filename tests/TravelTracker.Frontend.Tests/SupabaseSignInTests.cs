using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises Supabase authentication through real forms, JWT validation and revocable cookie middleware. */
public sealed class SupabaseSignInTests
{
    [Fact]
    public async Task ProfileAndCredentialFormsUseTheCurrentVerifiedEmailInsteadOfAnOlderTokenOrContactEmail()
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.CurrentEmail = "verified-new@example.test";
        using var browser = host.CreateBrowser();

        using var login = await LoginAsync(browser);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var page = await browser.GetStringAsync("/account");

        Assert.Contains("verified-new@example.test", page);
        Assert.DoesNotContain("traveller@example.test", page);
        Assert.DoesNotContain("Contact email", page);
        Assert.Contains("Request email change", page);
    }

    [Fact]
    public async Task MissingVerifiedProviderEmailCannotStartASession()
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.CurrentEmail = null;
        using var browser = host.CreateBrowser();

        using var login = await LoginAsync(browser);

        Assert.Equal("/sign-in?error=credentials", login.Headers.Location?.OriginalString);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("/account", false)]
    [InlineData("/account/", false)]
    [InlineData("/account", true)]
    [InlineData("/account/", true)]
    public async Task AccountRouteAliasesKeepProfileAndSignInStateNonCacheable(string path, bool signedIn)
    {
        using var host = new SupabaseFrontendFactory();
        using var browser = host.CreateBrowser();
        if (signedIn) await LoginAsync(browser);

        using var response = await browser.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var markup = await response.Content.ReadAsStringAsync();
        Assert.Contains(signedIn ? "traveller_1" : "Sign in to view your profile", markup);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
    }

    [Theory]
    [InlineData("/auth/v1/token", 500)]
    [InlineData("/auth/v1/token", 503)]
    [InlineData("/auth/v1/token", 429)]
    [InlineData("/auth/v1/token", -1)]
    [InlineData("/auth/v1/.well-known/jwks.json", 500)]
    [InlineData("/auth/v1/.well-known/jwks.json", 503)]
    [InlineData("/auth/v1/.well-known/jwks.json", 429)]
    [InlineData("/auth/v1/user", 500)]
    [InlineData("/auth/v1/user", 503)]
    [InlineData("/auth/v1/user", 429)]
    public async Task ProviderServiceFailuresDoNotBlameCredentialsOrStartASession(string path, int status)
    {
        using var fixture = new SupabaseFrontendFactory();
        using var host = WithProviderFailure(fixture, path, status);
        using var browser = host.CreateDefaultClient(new CookieJarHandler(TimeProvider.System));
        var response = await LoginAsync(browser);

        Assert.Equal(status == 429 ? "/sign-in?error=rate-limit" : "/sign-in?error=unavailable", response.Headers.Location?.OriginalString);
        var page = await browser.GetStringAsync(response.Headers.Location);
        Assert.Contains(status == 429 ? "Too many authentication attempts" : "temporarily unavailable", page);
        Assert.DoesNotContain("Check your email and password", page);
        Assert.DoesNotContain("private-provider-diagnostic", page);
        Assert.DoesNotContain("fixture-password", page);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("/auth/v1/token")]
    [InlineData("/auth/v1/user")]
    public async Task ProviderUnauthorizedStillShowsCredentialFailure(string path)
    {
        using var fixture = new SupabaseFrontendFactory();
        using var host = WithProviderFailure(fixture, path, 401);
        using var browser = host.CreateDefaultClient(new CookieJarHandler(TimeProvider.System));
        var response = await LoginAsync(browser);

        Assert.Equal("/sign-in?error=credentials", response.Headers.Location?.OriginalString);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("register", 503)]
    [InlineData("register", 429)]
    [InlineData("confirm-email", 503)]
    [InlineData("confirm-email", 429)]
    public async Task RegistrationAndConfirmationReportProviderFailuresWithoutClaimingSuccess(string action, int status)
    {
        using var fixture = new SupabaseFrontendFactory();
        using var host = WithProviderFailure(fixture, action == "register" ? "/auth/v1/signup" : "/auth/v1/verify", status);
        using var browser = host.CreateDefaultClient(new CookieJarHandler(TimeProvider.System));
        var page = await browser.GetStringAsync("/sign-in");
        var response = await browser.PostAsync("/auth/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "traveller@example.test", ["password"] = "fixture-password", ["passwordConfirmation"] = "fixture-password",
            ["token_hash"] = "confirmation-hash-fixture", ["type"] = "email",
            ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page)
        }));

        var location = response.Headers.Location?.OriginalString;
        Assert.Equal($"/sign-in?{(action == "register" ? "mode=register&" : "")}error={(status == 429 ? "rate-limit" : "unavailable")}", location);
        var failure = await browser.GetStringAsync(response.Headers.Location);
        Assert.DoesNotContain("private-provider-diagnostic", failure);
        Assert.DoesNotContain("Your email is confirmed", failure);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    /**
     * Overrides one provider response while keeping real forms, signed JWTs and cookie middleware.
     * @param fixture Supplies valid provider answers for the remaining requests.
     * @param path The provider path to fail.
     * @param status The HTTP status, or -1 for a connection failure.
     * @return An isolated frontend host with the selected failure.
     */
    static WebApplicationFactory<Program> WithProviderFailure(SupabaseFrontendFactory fixture, string path, int status) =>
        fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient<SupabaseAuthClient>().ConfigurePrimaryHttpMessageHandler(() => new StubHandler(async (request, token) =>
            {
                if (request.RequestUri!.AbsolutePath == path)
                {
                    if (status == -1) throw new HttpRequestException("private-provider-diagnostic");
                    return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("private-provider-diagnostic") };
                }
                using var provider = new HttpMessageInvoker(fixture.Provider, disposeHandler: false);
                return await provider.SendAsync(request, token);
            }))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidAsymmetricTokenCreatesHttpOnlySessionAndIgnoresUntrustedMetadata(bool ec)
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.UseEcKey = ec;
        var browser = host.CreateBrowser();
        var result = await LoginAsync(browser);
        Assert.Equal("/account", result.Headers.Location?.OriginalString);
        var cookie = string.Join(' ', result.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(host.Provider.LastAccessToken!, cookie);
        var account = await browser.GetStringAsync("/account");
        Assert.Contains("traveller_1", account);
        Assert.DoesNotContain("administrator", account, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("signature")]
    [InlineData("expired")]
    [InlineData("unverified")]
    [InlineData("service")]
    [InlineData("anonymous")]
    public async Task RejectedIdentityNeverStartsASession(string failure)
    {
        using var host = new SupabaseFrontendFactory();
        switch (failure)
        {
            case "issuer": host.Provider.Issuer = "https://other-project.supabase.test/auth/v1"; break;
            case "audience": host.Provider.Audience = "other-api"; break;
            case "signature": host.Provider.InvalidSignature = true; break;
            case "expired": host.Provider.Expired = true; break;
            case "unverified": host.Provider.Verified = false; break;
            case "service": host.Provider.Scope = "users.access"; break;
            case "anonymous": host.Provider.Anonymous = true; break;
        }
        var browser = host.CreateBrowser();
        var result = await LoginAsync(browser);
        Assert.Equal("/sign-in?error=credentials", result.Headers.Location?.OriginalString);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Fact]
    public async Task InvalidPasswordShowsOnlyFixedDiagnostics()
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.RejectPassword = true;
        var browser = host.CreateBrowser();
        var result = await LoginAsync(browser);
        Assert.Equal("/sign-in?error=credentials", result.Headers.Location?.OriginalString);
        var page = await browser.GetStringAsync(result.Headers.Location);
        Assert.DoesNotContain("private-provider-diagnostic", page);
        Assert.Contains("Check your email and password", page);
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    [InlineData("/auth/logout")]
    [InlineData("/auth/confirm-email")]
    [InlineData("/auth/change-credential")]
    public async Task CredentialAndLogoutPostsRequireAntiforgery(string path)
    {
        using var host = new SupabaseFrontendFactory();
        var browser = host.CreateBrowser();
        var result = await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["email"] = "traveller@example.test", ["password"] = "fixture-password" }));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, host.Provider.PasswordRequests + host.Provider.SignupRequests + host.Provider.LogoutRequests);
    }

    [Fact]
    public async Task SignupRequiresConfirmationAndDoesNotIssueAnAuthenticatedCookie()
    {
        using var host = new SupabaseFrontendFactory();
        var browser = host.CreateBrowser();
        var page = await browser.GetStringAsync("/sign-in?mode=register");
        var result = await browser.PostAsync("/auth/register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "new@example.test", ["password"] = "fixture-password", ["passwordConfirmation"] = "fixture-password",
            ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page)
        }));
        Assert.Equal("/sign-in?message=confirmation", result.Headers.Location?.OriginalString);
        Assert.Equal(1, host.Provider.SignupRequests);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SignoutRevokesLocalCookieEvenWhenSupabaseIsUnavailable(bool unavailable)
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.LogoutUnavailable = unavailable;
        var browser = host.CreateBrowser();
        await LoginAsync(browser);
        var page = await browser.GetStringAsync("/account");
        Assert.Contains("traveller_1", page);
        var result = await browser.PostAsync("/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page) }));
        Assert.Equal(unavailable ? "/account?signedout=local" : "/account", result.Headers.Location?.OriginalString);
        Assert.Equal(1, host.Provider.LogoutRequests);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Fact]
    public async Task LogoutAlsoRejectsASecondTabsPreviouslyIssuedCookie()
    {
        using var host = new SupabaseFrontendFactory();
        var browser = host.CreateBrowser();
        var login = await LoginAsync(browser);
        var sessionCookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        var secondTab = host.CreateBrowser();
        secondTab.DefaultRequestHeaders.Add("Cookie", sessionCookie);
        Assert.Contains("traveller_1", await secondTab.GetStringAsync("/account"));
        var page = await browser.GetStringAsync("/account");
        await browser.PostAsync("/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page) }));
        Assert.Contains("Sign in to view your profile", await secondTab.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("email", false)]
    [InlineData("email", true)]
    [InlineData("email_change", false)]
    public async Task EmailConfirmationExchangesOnlyTheHashAndNeverStartsASession(string type, bool rejected)
    {
        using var host = new SupabaseFrontendFactory();
        host.Provider.ConfirmationRejected = rejected;
        var browser = host.CreateBrowser();
        var page = await browser.GetAsync($"/auth/confirm?token_hash=confirmation-hash-fixture&type={type}");
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        Assert.Equal("no-referrer", Assert.Single(page.Headers.GetValues("Referrer-Policy")));
        var result = await browser.PostAsync("/auth/confirm-email", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token_hash"] = "confirmation-hash-fixture", ["type"] = type,
            ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(await page.Content.ReadAsStringAsync())
        }));
        var expected = rejected ? "/sign-in?error=confirmation"
            : type == "email_change" ? "/account?credential=link-accepted" : "/sign-in?message=confirmed";
        Assert.Equal(expected, result.Headers.Location?.OriginalString);
        Assert.Equal(1, host.Provider.ConfirmationRequests);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("password", "accepted", "password-changed", "password", true)]
    [InlineData("email", "accepted", "verify-email", "email", true)]
    [InlineData("password", "provider-rejected", "rejected", "password", false)]
    [InlineData("email", "other-owner", "rejected", null, false)]
    [InlineData("password", "invalid", "invalid", null, false)]
    public async Task CredentialChangesRequireFreshOwnerAuthenticationAndRevokeOnlyAfterSuccess(
        string operation, string scenario, string outcome, string? writtenField, bool signedOut)
    {
        using var host = new SupabaseFrontendFactory();
        var browser = host.CreateBrowser();
        using var login = await LoginAsync(browser);
        var secondTab = host.CreateBrowser();
        secondTab.DefaultRequestHeaders.Add("Cookie", string.Join("; ",
            login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0])));
        var account = await browser.GetStringAsync("/account");
        var form = new Dictionary<string, string>
        {
            ["operation"] = operation, ["loginEmail"] = "traveller@example.test",
            ["currentPassword"] = "fixture-password",
            ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(account)
        };
        if (operation == "email") form["newEmail"] = "updated@example.test";
        else
        {
            form["newPassword"] = "updated-password";
            form["passwordConfirmation"] = scenario == "invalid" ? "different" : "updated-password";
        }
        host.Provider.CredentialChangeRejected = scenario == "provider-rejected";
        if (scenario == "other-owner") host.Provider.CurrentSubject = Guid.NewGuid().ToString();

        using var result = await browser.PostAsync("/auth/change-credential", new FormUrlEncodedContent(form));

        Assert.Equal($"/account?credential={outcome}", result.Headers.Location?.OriginalString);
        Assert.Equal(writtenField, host.Provider.CredentialField);
        Assert.Equal(scenario == "invalid" ? 1 : 2, host.Provider.PasswordRequests);
        var secondPage = await secondTab.GetStringAsync("/account");
        Assert.Equal(signedOut, secondPage.Contains("Sign in to view your profile", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("sb_secret_privileged")]
    [InlineData("not-a-public-key")]
    public void PrivilegedOrMalformedKeysCannotConfigureBrowserSignIn(string key)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Supabase:Url"] = FakeSupabaseAuth.Origin, ["Supabase:PublishableKey"] = key }).Build();
        Assert.Null(SupabaseSettings.TryRead(configuration));
    }

    [Theory]
    [InlineData("Supabase", false)]
    [InlineData(null, false)]
    [InlineData("unknown-provider", true)]
    public async Task InvalidProviderConfigurationCannotDowngradeToLegacyAuth0(string? provider, bool validSettings)
    {
        using var host = new SupabaseFrontendFactory(provider, validSettings);
        var browser = host.CreateBrowser();
        var response = await browser.GetAsync("/auth/login");
        Assert.Null(response.Headers.Location);
        Assert.Contains("Sign-in is temporarily unavailable", await browser.GetStringAsync("/account"));
    }

    [Fact]
    public async Task ExcessiveCredentialSubmissionsAreRateLimitedBeforeCallingTheProvider()
    {
        using var host = new SupabaseFrontendFactory();
        var browser = host.CreateBrowser();
        var page = await browser.GetStringAsync("/sign-in");
        var form = new Dictionary<string, string>
        { ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page) };
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var result = await browser.PostAsync("/auth/login", new FormUrlEncodedContent(form));
            Assert.Equal("/sign-in?error=credentials", result.Headers.Location?.OriginalString);
        }
        var limited = await browser.PostAsync("/auth/login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(0, host.Provider.PasswordRequests);
    }

    /**
     * Submits the production password form using its current antiforgery token.
     * @param browser Cookie-keeping test browser.
     * @return The login response without following redirects.
     */
    static async Task<HttpResponseMessage> LoginAsync(HttpClient browser)
    {
        var page = await browser.GetStringAsync("/sign-in");
        return await browser.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = "traveller@example.test", ["password"] = "fixture-password",
            ["__RequestVerificationToken"] = FrontendFactory.AntiforgeryTokenFrom(page)
        }));
    }
}
