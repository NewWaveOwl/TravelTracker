using System.Net;
using System.Security.Claims;
using System.Text.Json;
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
    static readonly string Profile = """{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"traveller_1","email":"traveller@example.test","address":"1 Main St"}""";
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public AccountTests() => authentication = new(new SessionRegistry(clock));

    void Configure(bool configured = true, StubHandler? handler = null)
    {
        Services.AddSingleton(new AuthSetup(configured ? new Auth0Settings("tenant.auth0.com", "client", null, "traveltracker-api", Auth0Settings.DefaultScope) : null));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(authentication.Sessions);
        Services.AddSingleton(new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "http://localhost:5105" }));
        Services.AddSingleton(new ServiceClient(new HttpClient(handler ?? StubHandler.Status(200, Profile)), NullLogger<ServiceClient>.Instance));
        Services.AddScoped<ProtectedClient>();
        Services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        Services.AddScoped<TravelTracker.Frontend.Api.Users.UsersClient>();
        Services.AddScoped<TravelTracker.Frontend.Services.AdminAccessState>();
        Services.AddLogging();
    }

    [Fact]
    public void SignedOutCopyDoesNotPromiseLocalProfileOrTripStorage()
    {
        Configure();
        var page = Render<Account>();
        Assert.Contains("Sign in to view your profile", page.Markup);
        Assert.Contains("connected to your TravelTracker account", page.Markup);
        Assert.DoesNotContain("stay on this device", page.Markup);
    }

    [Fact]
    public void SetupPendingDoesNotOfferSignIn()
    {
        Configure(configured: false);
        var page = Render<Account>();
        Assert.Contains("Sign-in is temporarily unavailable", page.Markup);
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void SignInFailureUsesFixedSafeCopy()
    {
        Configure();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("account?signin=not-authorized");
        var page = Render<Account>();
        Assert.Contains("Unable to sign in", page.Markup);
        Assert.Contains("authorize its access", page.Markup);
        Assert.DoesNotContain("not-authorized", page.Markup);
    }

    [Fact]
    public void ExpiredSessionReturnsToSignIn()
    {
        Configure();
        authentication.SignIn("expired-token", clock.GetUtcNow().AddMinutes(-1));
        var page = Render<Account>();
        Assert.Contains("Sign in to view your profile", page.Markup);
        Assert.Contains("session has expired", page.Markup);
    }

    [Fact]
    public void RejectedUsersTokenClearsPrivateProfile()
    {
        Configure(handler: StubHandler.Status(401));
        authentication.SignIn("rejected-token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("Sign in to view your profile", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.Contains("session was rejected", page.Markup);
    }

    [Fact]
    public void SharedSessionRevocationClearsTheDisplayedProfile()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var provider = new MutableAuthentication(authentication.User);
        Services.AddSingleton<AuthenticationStateProvider>(provider);
        var page = Render<Account>();
        page.WaitForAssertion(() => Assert.Contains("traveller_1", page.Markup));
        authentication.Sessions.Revoke(SessionRegistry.IdFor(authentication.User));
        provider.Notify(authentication.User);
        page.WaitForAssertion(() => Assert.Contains("Sign in to view your profile", page.Markup));
        Assert.DoesNotContain("traveller_1", page.Markup);
    }

    [Fact]
    public void SignedInProfileShowsReadOnlyRoleAndStatusAndOffersEdit()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.Contains("traveller_1", page.Markup);
        Assert.Contains("Contact email", page.Markup);
        Assert.Contains("Traveller", page.Markup);
        Assert.Contains("Active", page.Markup);
        page.Find("button").Click();
        Assert.NotNull(page.Find("#profile-username"));
        Assert.Empty(page.FindAll("input[name='role'], input[name='status']"));
    }

    [Fact]
    public void MissingWriteScopeMakesProfileReadOnly()
    {
        Configure();
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5), "users.read");
        var page = Render<Account>();
        Assert.Contains("does not have permission to edit", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Edit profile", StringComparison.Ordinal));
    }

    [Fact]
    public void UsersReadForbiddenShowsDeniedStateWithoutProfileData()
    {
        Configure(handler: StubHandler.Status(403));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));

        var page = Render<Account>();

        Assert.Contains("Profile access denied", page.Markup);
        Assert.Contains("does not have access to a profile", page.Markup);
        Assert.DoesNotContain("traveller_1", page.Markup);
        Assert.DoesNotContain("Profile details", page.Markup);
    }

    [Fact]
    public async Task SuccessfulSavePatchesOnlyChangedOwnProfileFieldsAndRefreshesFromResponse()
    {
        HttpRequestMessage? patched = null;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                patched = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"publicId":"00000000-0000-0000-0000-000000000101","role":"traveller","status":"active","username":"new_name","email":"traveller@example.test","address":null}""") });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) });
        });
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-username").Change("new_name");
        page.Find("#profile-address").Change("");
        page.Find("button[type='submit']").Click();
        page.WaitForAssertion(() => Assert.Contains("Your profile has been updated", page.Markup));
        Assert.Equal("PATCH", patched!.Method.Method);
        Assert.Equal("/api/v1/users/me", patched.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(await patched.Content!.ReadAsStringAsync());
        Assert.Equal("new_name", body.RootElement.GetProperty("username").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("address").ValueKind);
        Assert.DoesNotContain("role", body.RootElement.GetRawText());
        Assert.DoesNotContain("status", body.RootElement.GetRawText());
    }

    [Fact]
    public void ForbiddenPatchKeepsDraftVisibleAndDoesNotReportSuccess()
    {
        var handler = new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Profile) }));
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-username").Change("draft_username");

        page.Find("button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains("no longer permits profile changes", page.Markup));
        Assert.Equal("draft_username", page.Find("#profile-username").GetAttribute("value"));
        Assert.DoesNotContain("Your profile has been updated", page.Markup);
    }

    [Fact]
    public void ValidationKeepsFormAndDoesNotSendInvalidUsername()
    {
        var handler = StubHandler.Status(200, Profile);
        Configure(handler: handler);
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        page.Find("button").Click();
        page.Find("#profile-username").Change("x!");
        page.Find("button[type='submit']").Click();
        Assert.Contains("Use only letters", page.Markup);
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    [Fact]
    public void BannedProfileIsVisibleButReadOnly()
    {
        Configure(handler: StubHandler.Status(200, Profile.Replace("active", "banned")));
        authentication.SignIn("token", clock.GetUtcNow().AddMinutes(5));
        var page = Render<Account>();
        Assert.Contains("Read-only profile", page.Markup);
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Contains("Edit profile", StringComparison.Ordinal));
    }

    sealed class MutableAuthentication(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        ClaimsPrincipal user = user;
        public void Notify(ClaimsPrincipal next)
        {
            user = next;
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
