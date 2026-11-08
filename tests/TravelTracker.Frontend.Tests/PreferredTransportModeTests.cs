using System.Net;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Transport;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** proves the one applied preference: the saved transport mode is the default for new transport entries. */
public sealed class PreferredTransportModeTests : BunitContext
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-06T10:00:00Z"));

    /**
     * builds the protected stack with Settings and Transport both reachable through one stub.
     * @param settingsReply Answers the settings read; the transport reads always return an empty catalogue.
     * @return The switchable authentication for the scenario.
     */
    (SwitchableAuthentication Authentication, SessionRegistry Sessions) Configure(Func<HttpRequestMessage, HttpResponseMessage> settingsReply)
    {
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, "catalogue.read catalogue.write settings.read"));
        var handler = new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/v1/settings"
            ? settingsReply(request)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json") }));
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Transport"] = "http://localhost:5105", ["Settings"] = "http://localhost:5107" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new TravelTracker.Frontend.Api.Transport.TransportClient(client));
        Services.AddSingleton(new SettingsClient(client));
        Services.AddScoped<UserPreferences>();
        Services.AddLogging();
        return (authentication, sessions);
    }

    static HttpResponseMessage SavedSettings(string mode) => new(HttpStatusCode.OK)
    {
        Content = System.Net.Http.Json.JsonContent.Create(new SettingsDto("system", 0, 0, 2, mode, "UTC")),
    };
    // the read must carry the revision tag, as the real service always does
    static HttpResponseMessage SavedSettingsWithTag(string mode)
    {
        var response = SavedSettings(mode);
        response.Headers.TryAddWithoutValidation("ETag", "\"0\"");
        return response;
    }

    [Fact]
    public void NewEntryModeStartsAtTheSavedTransportPreference()
    {
        Configure(_ => SavedSettingsWithTag("train"));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Equal("train", page.Find("#new-transport-code").GetAttribute("value")));
    }

    [Fact]
    public void UnreadablePreferenceKeepsCarAndDoesNotBreakTheWorkspace()
    {
        Configure(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No saved transport entries are available.", page.Markup));
        Assert.Equal("car", page.Find("#new-transport-code").GetAttribute("value"));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public async Task PreferencesAreReadOncePerSignInAndForgottenOnSignOut()
    {
        var reads = 0;
        var (authentication, _) = Configure(_ =>
        {
            reads++;
            return SavedSettingsWithTag("bike");
        });
        var client = Services.GetRequiredService<SettingsClient>();
        using var preferences = new UserPreferences(client, authentication, NullLogger<UserPreferences>.Instance);
        Assert.Equal("bike", (await preferences.LoadAsync())?.PreferredTransportMode);
        Assert.Equal("bike", (await preferences.LoadAsync())?.PreferredTransportMode);
        Assert.Equal(1, reads);
        authentication.SignOut();
        Assert.Null(preferences.Current);
    }
}
