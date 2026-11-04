using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

public sealed class AccountTests : BunitContext
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public AccountTests() => authentication = new(new SessionRegistry(clock));

    void Configure(bool signInConfigured, StubHandler? handler = null)
    {
        Services.AddSingleton(new AuthSetup(signInConfigured ? new Auth0Settings("tenant.auth0.com", "client", null, "traveltracker-api", Auth0Settings.DefaultScope) : null));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(authentication.Sessions);
        Services.AddSingleton(new ServiceDirectory(new Dictionary<string, string> { ["Transport"] = "http://localhost:5105" }));
        Services.AddSingleton(new ServiceClient(new HttpClient(handler ?? StubHandler.Status(200, "{}")), NullLogger<ServiceClient>.Instance));
        Services.AddScoped<ProtectedClient>();
        Services.AddLogging();
    }

    [Fact]
    public void ShowsSetupPendingWhenSignInIsNotConfigured()
    {
        Configure(signInConfigured: false);

        var page = Render<Account>();

        Assert.Equal("Service unavailable", page.Find("h2").TextContent);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void OffersSignInWhenSignedOut()
    {
        Configure(signInConfigured: true);

        var page = Render<Account>();

        Assert.Equal("Sign in", page.Find("section.form-panel h2").TextContent);
        Assert.Empty(page.FindAll("form[action='auth/logout']"));
    }

    [Fact]
    public void ShowsOnlyTheFixedMessageForASignInFailure()
    {
        Configure(signInConfigured: true);
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("account?signin=not-authorized");

        var page = Render<Account>();

        Assert.Equal("Unable to sign in", page.Find(".feedback h2").TextContent);
        Assert.Contains("authorize its access", page.Find(".feedback p").TextContent);
    }

    [Fact]
    public void OffersCheckAndSignOutWhenSignedIn()
    {
        Configure(signInConfigured: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));

        var page = Render<Account>();

        Assert.Equal("You are signed in", page.Find("section.form-panel h2").TextContent);
        Assert.Equal("POST", page.Find("form[action='auth/logout']").GetAttribute("method")!.ToUpperInvariant());
    }

    [Fact]
    public void ReportsAnExpiredSessionAndOffersSignIn()
    {
        Configure(signInConfigured: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(-1));

        var page = Render<Account>();

        Assert.Equal("Sign in", page.Find("section.form-panel h2").TextContent);
        Assert.Contains("expired", page.Find("p[role='status']").TextContent);
    }

    [Fact]
    public void CheckingAccessReportsSuccess()
    {
        Configure(signInConfigured: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();

        page.Find(".actions button").Click();

        page.WaitForAssertion(() => Assert.Equal("Your protected request completed successfully.", page.Find(".feedback p").TextContent));
    }

    [Fact]
    public void ARejectedTokenReturnsToSignIn()
    {
        Configure(signInConfigured: true, StubHandler.Status(401));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();

        page.Find(".actions button").Click();

        page.WaitForAssertion(() => Assert.Equal("Sign in", page.Find("section.form-panel h2").TextContent));
    }

    [Fact]
    public void AMissingPermissionKeepsTheSessionAlive()
    {
        Configure(signInConfigured: true, StubHandler.Status(403));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();

        page.Find(".actions button").Click();

        page.WaitForAssertion(() => Assert.Contains("permission", page.Find(".feedback p").TextContent));
        Assert.Equal("You are signed in", page.Find("section.form-panel h2").TextContent);
    }

    [Fact]
    public void SharedSessionRevocationUpdatesTheOpenAccountPage()
    {
        Configure(signInConfigured: true);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        using var provider = new SessionAuthenticationStateProvider(authentication.Sessions);
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(authentication.User)));
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        var page = Render<Account>();
        page.Find(".actions button").Click();
        page.WaitForAssertion(() => Assert.Contains("completed successfully", page.Markup));

        authentication.Sessions.Revoke(SessionRegistry.IdFor(authentication.User));

        page.WaitForAssertion(() => Assert.Equal("Sign in", page.Find("section.form-panel h2").TextContent));
        Assert.DoesNotContain("completed successfully", page.Markup);
        Assert.Empty(page.FindAll("form[action='auth/logout']"));
    }

    [Fact]
    public void RejectedSessionStaysSignedOutWhenTheAccountPageIsRecreated()
    {
        Configure(signInConfigured: true, StubHandler.Status(401));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        using var provider = new SessionAuthenticationStateProvider(authentication.Sessions);
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(authentication.User)));
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        var page = Render<Account>();
        page.Find(".actions button").Click();
        page.WaitForAssertion(() => Assert.Equal("Sign in", page.Find("section.form-panel h2").TextContent));
        page.Dispose();

        var returned = Render<Account>();

        Assert.Equal("Sign in", returned.Find("section.form-panel h2").TextContent);
        Assert.DoesNotContain("Check API access", returned.Markup);
    }
}
