using System.Net;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises password sign-in and registration through the real forms, the Users API client and the revocable cookie middleware. */
public sealed class PasswordSignInTests
{
    static FormUrlEncodedContent Form(string token, params (string Name, string Value)[] fields) =>
        new(fields.Select(field => KeyValuePair.Create(field.Name, field.Value)).Append(new("__RequestVerificationToken", token)));

    [Fact]
    public async Task ValidSignInCreatesAnHttpOnlySessionWithoutExposingTheToken()
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var result = await host.SignInAsync(browser);
        Assert.Equal("/account", result.Headers.Location?.OriginalString);
        var cookie = string.Join(' ', result.Headers.GetValues("Set-Cookie"));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(host.Users.LastToken!, cookie);
        Assert.Contains("traveller_1", await browser.GetStringAsync("/account"));
        Assert.Equal("traveller_1", host.Users.LastUsername);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task RefusedCredentialsShowOnlyFixedDiagnostics(HttpStatusCode refusal)
    {
        using var host = new FrontendFactory();
        host.Users.Reject = refusal;
        var browser = host.CreateBrowser();
        var result = await host.SignInAsync(browser);
        Assert.Equal("/sign-in?error=credentials", result.Headers.Location?.OriginalString);
        var page = await browser.GetStringAsync(result.Headers.Location);
        Assert.DoesNotContain("private-provider-diagnostic", page);
        Assert.Contains("Check your username and password", page);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Fact]
    public async Task AnUnavailableUsersServiceShowsAFixedMessageAndLeaksNothing()
    {
        using var host = new FrontendFactory();
        host.Users.Unreachable = true;
        var browser = host.CreateBrowser();
        var result = await host.SignInAsync(browser);
        Assert.Equal("/sign-in?error=unavailable", result.Headers.Location?.OriginalString);
        var page = await browser.GetStringAsync(result.Headers.Location);
        Assert.Contains("temporarily unavailable", page);
        Assert.DoesNotContain("users-service-private-diagnostic", page);
    }

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    [InlineData("/auth/logout")]
    public async Task CredentialAndLogoutPostsRequireAntiforgery(string path)
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var result = await browser.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        { ["username"] = "traveller_1", ["password"] = "fixture-password" }));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, host.Users.LoginRequests + host.Users.RegisterRequests);
    }

    [Fact]
    public async Task RegistrationSignsTheNewAccountStraightIn()
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var token = FrontendFactory.AntiforgeryTokenFrom(await browser.GetStringAsync("/sign-in?mode=register"));
        var result = await browser.PostAsync("/auth/register", Form(token, ("username", "new_user"), ("password", "fixture-password"), ("passwordConfirmation", "fixture-password")));
        Assert.Equal("/account", result.Headers.Location?.OriginalString);
        Assert.Equal(1, host.Users.RegisterRequests);
        Assert.Contains("traveller_1", await browser.GetStringAsync("/account"));
    }

    [Theory]
    [InlineData("fixture-password", "different-password")]
    [InlineData("short", "short")]
    public async Task RegistrationChecksPasswordsLocallyBeforeCallingUsers(string password, string confirmation)
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var token = FrontendFactory.AntiforgeryTokenFrom(await browser.GetStringAsync("/sign-in?mode=register"));
        var result = await browser.PostAsync("/auth/register", Form(token, ("username", "new_user"), ("password", password), ("passwordConfirmation", confirmation)));
        Assert.Equal("/sign-in?mode=register&error=registration", result.Headers.Location?.OriginalString);
        Assert.Equal(0, host.Users.RegisterRequests);
    }

    [Fact]
    public async Task ATakenUsernameIsRefusedWithoutStartingASession()
    {
        using var host = new FrontendFactory();
        host.Users.Reject = HttpStatusCode.Conflict;
        var browser = host.CreateBrowser();
        var token = FrontendFactory.AntiforgeryTokenFrom(await browser.GetStringAsync("/sign-in?mode=register"));
        var result = await browser.PostAsync("/auth/register", Form(token, ("username", "taken"), ("password", "fixture-password"), ("passwordConfirmation", "fixture-password")));
        Assert.Equal("/sign-in?mode=register&error=registration", result.Headers.Location?.OriginalString);
        Assert.Contains("Sign in to view your profile", await browser.GetStringAsync("/account"));
    }

    [Fact]
    public async Task LogoutAlsoRejectsASecondTabsPreviouslyIssuedCookie()
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var login = await host.SignInAsync(browser);
        var sessionCookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        var secondTab = host.CreateBrowser();
        secondTab.DefaultRequestHeaders.Add("Cookie", sessionCookie);
        Assert.Contains("traveller_1", await secondTab.GetStringAsync("/account"));
        var page = await browser.GetStringAsync("/account");
        await browser.PostAsync("/auth/logout", Form(FrontendFactory.AntiforgeryTokenFrom(page)));
        Assert.Contains("Sign in to view your profile", await secondTab.GetStringAsync("/account"));
    }

    [Fact]
    public async Task ExcessiveCredentialSubmissionsAreRateLimitedBeforeCallingUsers()
    {
        using var host = new FrontendFactory();
        var browser = host.CreateBrowser();
        var token = FrontendFactory.AntiforgeryTokenFrom(await browser.GetStringAsync("/sign-in"));
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var result = await browser.PostAsync("/auth/login", Form(token));
            Assert.Equal("/sign-in?error=credentials", result.Headers.Location?.OriginalString);
        }
        var limited = await browser.PostAsync("/auth/login", Form(token));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(0, host.Users.LoginRequests);
    }
}
