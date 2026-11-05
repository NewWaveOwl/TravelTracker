using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Fake validated identities test live Users authorization responses and UI behavior, not bearer validation. */
public sealed class UserAdministrationTests : BunitContext
{
    const string TravellerEmail = "traveller@example.test";
    static readonly UserProfile Administrator = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "admin@example.test", "administrator", "active");
    static readonly UserProfile Traveller = new(Guid.Parse("00000000-0000-0000-0000-000000000002"), TravellerEmail, "traveller", "active");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
    PublishingAuthentication notifier = default!;

    /**
     * Connects the actual public Users client to isolated, deterministic HTTP responses.
     * @param handler Supplies service-owned profiles, lists and role-change responses.
     * @return A revocable fake validated session for sign-out and expiry checks.
     */
    SwitchableAuthentication Configure(StubHandler handler)
    {
        var sessions = new SessionRegistry(clock);
        var auth = new SwitchableAuthentication(sessions);
        auth.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        notifier = new PublishingAuthentication(auth);
        var protectedClient = new ProtectedClient(notifier,
            new ServiceDirectory(new Dictionary<string, string> { ["Users"] = "http://localhost:5106" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(notifier);
        Services.AddSingleton(new UsersClient(protectedClient, new ConfigurationBuilder().Build()));
        Services.AddSingleton<AdminAccessState>();
        Services.AddLogging();
        return auth;
    }

    static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(body) };

    static HttpResponseMessage Self(UserProfile user) => Json(new { publicId = user.UserId, user.Role, user.Status });

    [Theory]
    [InlineData("traveller", "active")]
    [InlineData("administrator", "banned")]
    public void NonAdministratorsNeverRequestOrRenderPrivateUsers(string role, string standing)
    {
        var listCalls = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v1/users/me") listCalls++;
            return Task.FromResult(Self(Traveller with { Role = role, Status = standing }));
        }));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Contains("Administrator access required", panel.Markup));
        Assert.Equal(0, listCalls);
        Assert.DoesNotContain(TravellerEmail, panel.Markup);
        Assert.Empty(panel.FindAll("table"));
        Assert.Empty(panel.FindAll("form"));
    }

    [Fact]
    public void UsesTheServiceListAndDisablesDemotionOfTheLastActiveAdministrator()
    {
        Configure(new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(Administrator) : Json(new[] { Administrator, Traveller with { Email = null } }))));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        Assert.Equal("All users", panel.Find(".admin-tabs a").TextContent);
        Assert.Contains("(you)", panel.Find("tbody tr:first-child th").TextContent);
        Assert.Equal(Traveller.UserId.ToString(), panel.Find("tbody tr:last-child th").TextContent.Trim());
        Assert.True(panel.Find("tbody tr:first-child button").HasAttribute("disabled"));
        Assert.False(panel.Find("tbody tr:last-child button").HasAttribute("disabled"));
        Assert.Contains("registered with TravelTracker", panel.Markup);
    }

    [Fact]
    public async Task PromotionRequiresReasonAndOnlyPublishesConfirmedRefreshedRole()
    {
        var currentTraveller = Traveller;
        Dictionary<string, JsonElement>? payload = null;
        var listReads = 0;
        Configure(new StubHandler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Patch)
            {
                payload = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                currentTraveller = Traveller with { Role = "administrator" };
                return Json(currentTraveller);
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/me")) return Self(Administrator);
            listReads++;
            return Json(new[] { Administrator, currentTraveller });
        }));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        Assert.Contains("traveller → administrator", panel.Find(".admin-confirmation").TextContent);
        panel.Find(".admin-confirmation").Submit();
        Assert.Null(payload);
        Assert.Contains("Enter a reason", panel.Markup);
        panel.Find("#role-change-reason").Change("Approved support coverage");
        await panel.Find(".admin-confirmation").SubmitAsync();
        panel.WaitForAssertion(() => Assert.Contains("The list shows the saved roles", panel.Markup));
        Assert.Equal("administrator", panel.Find("tbody tr:last-child td").TextContent);
        Assert.Equal("administrator", payload!["role"].GetString());
        Assert.Equal("traveller", payload["expectedRole"].GetString());
        Assert.Equal("Approved support coverage", payload["reason"].GetString());
        Assert.Equal(2, listReads);
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    public void FailedRoleChangesPreserveReasonAndDoNotAlterDisplayedRoles(int failure)
    {
        Configure(new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Patch
            ? new HttpResponseMessage((HttpStatusCode)failure)
            : request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(Administrator) : Json(new[] { Administrator, Traveller }))));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        panel.Find("#role-change-reason").Change("Keep my reason for retry");
        panel.Find(".admin-confirmation").Submit();
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll("[role=alert]")));
        Assert.Equal("Keep my reason for retry", panel.Find("#role-change-reason").GetAttribute("value"));
        Assert.Equal("traveller", panel.Find("tbody tr:last-child td").TextContent);
        Assert.DoesNotContain("Role change confirmed", panel.Markup);
    }

    [Fact]
    public async Task SelfDemotionClearsPrivateUsersAndTheSharedAdministratorLink()
    {
        var me = Administrator;
        var otherAdmin = Traveller with { Role = "administrator" };
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Patch) { me = Administrator with { Role = "traveller" }; return Task.FromResult(Json(me)); }
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(me) : Json(new[] { Administrator, otherAdmin }));
        }));
        var navigation = Render<AdminNavigation>();
        navigation.WaitForAssertion(() => Assert.Single(navigation.FindAll("a[href=administrator]")));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:first-child button").Click();
        panel.Find("#role-change-reason").Change("Another administrator has taken over");
        await panel.Find(".admin-confirmation").SubmitAsync();
        panel.WaitForAssertion(() => Assert.Contains("no longer has access", panel.Markup));
        Assert.Empty(panel.FindAll("table"));
        Assert.DoesNotContain(TravellerEmail, panel.Markup);
        navigation.WaitForAssertion(() => Assert.Empty(navigation.FindAll("a")));
    }

    [Fact]
    public async Task OlderProfileReplyCannotRestoreAdministratorNavigationAfterDemotion()
    {
        var olderReply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        Configure(new StubHandler((_, _) => Interlocked.Increment(ref reads) == 1 ? olderReply.Task : Task.FromResult(Self(Administrator with { Role = "traveller" }))));
        var state = Services.GetRequiredService<AdminAccessState>();
        var older = state.RefreshAsync();
        await state.RefreshAsync();
        Assert.False(state.IsAdministrator);
        olderReply.SetResult(Self(Administrator));
        await older;
        Assert.False(state.IsAdministrator);
    }

    [Fact]
    public async Task SignOutImmediatelyClearsPrivateRowsAndConfirmation()
    {
        var auth = Configure(new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(Administrator) : Json(new[] { Administrator, Traveller }))));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        panel.Find("#role-change-reason").Change("A private reason");
        await panel.InvokeAsync(auth.SignOut);
        panel.WaitForAssertion(() => Assert.Empty(panel.FindAll("table")));
        Assert.Empty(panel.FindAll("form"));
        Assert.DoesNotContain(TravellerEmail, panel.Markup);
        Assert.DoesNotContain("A private reason", panel.Markup);
    }

    [Fact]
    public async Task AccountSwitchClearsPrivateRowsBeforeTheNewProfileRequestCompletes()
    {
        var pendingProfile = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var auth = Configure(new StubHandler((request, _) => request.RequestUri!.AbsolutePath.EndsWith("/me")
            ? Interlocked.Increment(ref reads) == 1 ? Task.FromResult(Self(Administrator)) : pendingProfile.Task
            : Task.FromResult(Json(new[] { Administrator, Traveller }))));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        panel.Find("#role-change-reason").Change("A private reason");
        await panel.InvokeAsync(() =>
        {
            auth.SignIn("replacement-token", clock.GetUtcNow().AddMinutes(10));
            notifier.Publish();
        });
        panel.WaitForAssertion(() => Assert.Equal(2, reads));
        Assert.False(pendingProfile.Task.IsCompleted);
        Assert.Empty(panel.FindAll("table"));
        Assert.Empty(panel.FindAll("form"));
        Assert.DoesNotContain(TravellerEmail, panel.Markup);
        pendingProfile.SetResult(Self(Traveller));
        panel.WaitForAssertion(() => Assert.Contains("Administrator access required", panel.Markup));
    }

    [Fact]
    public async Task DuplicateConfirmationSendsOneRequestAndKeepsTheReasonOnFailure()
    {
        var pendingChange = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Patch) { Interlocked.Increment(ref writes); return pendingChange.Task; }
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(Administrator) : Json(new[] { Administrator, Traveller }));
        }));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        panel.Find("#role-change-reason").Change("Coverage approved");
        var first = panel.Find(".admin-confirmation").SubmitAsync();
        panel.WaitForAssertion(() => Assert.Equal(1, writes));
        await panel.Find(".admin-confirmation").SubmitAsync();
        Assert.Equal(1, writes);
        Assert.True(panel.Find(".admin-confirmation button[type=submit]").HasAttribute("disabled"));
        pendingChange.SetResult(new(HttpStatusCode.ServiceUnavailable));
        await first;
        panel.WaitForAssertion(() => Assert.NotEmpty(panel.FindAll("[role=alert]")));
        Assert.Equal("Coverage approved", panel.Find("#role-change-reason").GetAttribute("value"));
    }

    [Fact]
    public void ServerPermissionFailureClearsPrivateUsersAndRoleControls()
    {
        Configure(new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Patch ? new HttpResponseMessage(HttpStatusCode.Forbidden)
            : request.RequestUri!.AbsolutePath.EndsWith("/me") ? Self(Administrator) : Json(new[] { Administrator, Traveller }))));
        var panel = Render<AdministratorPanel>();
        panel.WaitForAssertion(() => Assert.Equal(2, panel.FindAll("tbody tr").Count));
        panel.Find("tbody tr:last-child button").Click();
        panel.Find("#role-change-reason").Change("Role change rejected by live authority");
        panel.Find(".admin-confirmation").Submit();
        panel.WaitForAssertion(() => Assert.Contains("Administrator access required", panel.Markup));
        Assert.Empty(panel.FindAll("table"));
        Assert.Empty(panel.FindAll("form"));
        Assert.DoesNotContain(TravellerEmail, panel.Markup);
        Assert.False(Services.GetRequiredService<AdminAccessState>().IsAdministrator);
    }

    /** Adds a replacement-session notification to the existing fake identity helper. */
    sealed class PublishingAuthentication : AuthenticationStateProvider, IDisposable
    {
        readonly SwitchableAuthentication source;
        public PublishingAuthentication(SwitchableAuthentication source) { this.source = source; source.AuthenticationStateChanged += Changed; }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => source.GetAuthenticationStateAsync();
        public void Publish() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        void Changed(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);
        public void Dispose() => source.AuthenticationStateChanged -= Changed;
    }
}
