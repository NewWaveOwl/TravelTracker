using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Bunit;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** uses isolated validated-session fixtures; API tests independently prove real bearer validation. */
public sealed class AdminSetupTests : BunitContext
{
    const string Secret = "test-bootstrap-secret-with-at-least-thirty-two-characters";
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T12:00:00Z"));

    void Configure(StubHandler handler, bool signedIn = true)
    {
        var sessions = new SessionRegistry(clock);
        var auth = new SwitchableAuthentication(sessions);
        if (signedIn)
            auth.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminBootstrap:Secret"] = Secret }).Build();
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "https://localhost:5101" });
        var client = new ProtectedClient(auth, directory, new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<AuthenticationStateProvider>(auth);
        Services.AddSingleton(sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(new UsersClient(client, config));
        Services.AddLogging();
    }

    [Fact]
    public void AnonymousUsersCannotInitializeOrReadRoles()
    {
        var handler = StubHandler.Status(200, "{}");
        Configure(handler, false);
        var page = Render<AdminSetup>();
        page.WaitForAssertion(() => Assert.Contains("Sign in first", page.Markup));
        Assert.Empty(page.FindAll("button"));
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(503)]
    public void FailedAccountLookupShowsRecoverableErrorWithoutOfferingSetup(int status)
    {
        var failing = true;
        Configure(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(
            failing ? (HttpStatusCode)status : HttpStatusCode.NotFound))));
        var page = Render<AdminSetup>();

        Assert.Contains("Unable to check your account", page.Find("[role=alert]").TextContent);
        Assert.DoesNotContain("Sign in first", page.Markup);
        Assert.DoesNotContain("Administrator access confirmed", page.Markup);
        Assert.True(page.FindAll("button").Single(button => button.TextContent.Contains("Make this account")).HasAttribute("disabled"));

        failing = false;
        page.FindAll("button").Single(button => button.TextContent == "Retry account check").Click();
        Assert.Empty(page.FindAll("[role=alert]"));
        Assert.False(page.FindAll("button").Single(button => button.TextContent.Contains("Make this account")).HasAttribute("disabled"));
    }

    [Fact]
    public void RejectedAccountSessionShowsSignInWithoutFocusingAnAbsentError()
    {
        Configure(StubHandler.Status(401));
        var page = Render<AdminSetup>();

        Assert.Contains("Sign in first", page.Markup);
        Assert.Empty(page.FindAll("button"));
        Assert.DoesNotContain("Administrator access confirmed", page.Markup);
    }

    [Fact]
    public void DisplaysConfirmedRoleAndNeverRendersOperatorSecret()
    {
        var writes = 0;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            writes++;
            Assert.Equal(Secret, Assert.Single(request.Headers.GetValues("X-Bootstrap-Secret")));
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new UserProfile(Guid.Parse("00000000-0000-0000-0000-000000000801"), "admin@example.test", "administrator", "active")),
            });
        });
        Configure(handler);
        var page = Render<AdminSetup>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("button")));
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("Administrator access confirmed", page.Markup));
        Assert.Equal(1, writes);
        Assert.DoesNotContain(Secret, page.Markup);
    }

    [Fact]
    public void RejectedVerificationNeverClaimsAdministratorAccess()
    {
        Configure(new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Get ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden))));
        var page = Render<AdminSetup>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll("button")));
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("not allowed", page.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Administrator access confirmed", page.Markup);
        Assert.DoesNotContain(Secret, page.Markup);
    }

    [Fact]
    public async Task SetupSecretNeverGoesToAnInsecureDestination()
    {
        var handler = StubHandler.Status(200, "{}");
        using var httpClient = new HttpClient(handler);
        var client = new ServiceClient(httpClient, NullLogger<ServiceClient>.Instance);
        await Assert.ThrowsAsync<ApiError>(() => client.SendAsync<object>(HttpMethod.Post, "http://localhost:5101/api/v1/users/bootstrap-first-administrator", new { }, default, "test-token", Secret));
        Assert.Null(handler.LastRequest);
    }
}
