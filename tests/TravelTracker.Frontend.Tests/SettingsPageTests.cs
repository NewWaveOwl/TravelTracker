using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Settings page journeys against a stateful fake Settings service; authorization here is fake-identity only. */
public sealed class SettingsPageTests : BunitContext
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-06T10:00:00Z"));
    readonly FakeSettingsService service = new();

    /**
     * connects the real page to the fake service through the real protected HTTP stack.
     * @param write Whether the fake grant includes settings.write.
     * @param signedIn Whether the fake identity has an active session.
     * @return The switchable authentication for sign-out scenarios.
     */
    SwitchableAuthentication Configure(bool write = true, bool signedIn = true)
    {
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        if (signedIn) authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        if (signedIn) ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, write ? "settings.read settings.write" : "settings.read"));
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Settings"] = "http://localhost:5107" }),
            new ServiceClient(new HttpClient(service), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new SettingsClient(client));
        Services.AddScoped<UserPreferences>();
        Services.AddLogging();
        return authentication;
    }

    /**
     * waits until the saved form is visible.
     * @param page The rendered settings page.
     */
    static void WaitForForm(IRenderedComponent<Settings> page) => page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("form.settings-form")));

    [Fact]
    public void ShowsDocumentedDefaultsBeforeFirstSave()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        Assert.Equal("system", page.Find("#settings-theme").GetAttribute("value"));
        Assert.Equal("car", page.Find("#settings-transport").GetAttribute("value"));
        Assert.Equal("0", page.Find("#settings-latitude").GetAttribute("value"));
        Assert.Equal("0", page.Find("#settings-longitude").GetAttribute("value"));
        Assert.Equal("2", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.Equal("UTC", page.Find("#settings-timezone").GetAttribute("value"));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void SavesWithIfMatchConfirmsValuesAndTheyReappearAfterReload()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        page.Find("#settings-theme").Change("dark");
        page.Find("#settings-transport").Change("train");
        page.Find("#settings-latitude").Input("45.5");
        page.Find("#settings-longitude").Input("-73.5");
        page.Find("#settings-zoom").Input("5");
        page.Find("#settings-timezone").Input("America/Montreal");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Settings saved.", page.Markup));
        Assert.Equal(new SettingsDto("dark", 45.5m, -73.5m, 5, "train", "America/Montreal"), service.Stored);
        Assert.Equal("\"0\"", service.Requests.Single(request => request.Method == HttpMethod.Put).IfMatch);
        Assert.Equal("train", Services.GetRequiredService<UserPreferences>().Current?.PreferredTransportMode);
        page.Dispose();

        var reloaded = Render<Settings>();
        WaitForForm(reloaded);
        Assert.Equal("dark", reloaded.Find("#settings-theme").GetAttribute("value"));
        Assert.Equal("train", reloaded.Find("#settings-transport").GetAttribute("value"));
        Assert.Equal("45.5", reloaded.Find("#settings-latitude").GetAttribute("value"));
        Assert.Equal("5", reloaded.Find("#settings-zoom").GetAttribute("value"));
        Assert.Equal("America/Montreal", reloaded.Find("#settings-timezone").GetAttribute("value"));
    }

    [Fact]
    public void UnchangedFormSendsNoWrite()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("No changes to save.", page.Markup));
        Assert.Equal(0, service.AcceptedWrites);
        Assert.DoesNotContain(service.Requests, request => request.Method == HttpMethod.Put);
    }

    [Fact]
    public void ClientValidationBlocksTheRequestAndKeepsTheInput()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        page.Find("#settings-zoom").Input("21");
        page.Find("#settings-latitude").Input("91");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Check your details", page.Find("[role=alert]").TextContent));
        Assert.Equal(0, service.AcceptedWrites);
        Assert.DoesNotContain(service.Requests, request => request.Method == HttpMethod.Put);
        Assert.Equal("21", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.Equal("true", page.Find("#settings-zoom").GetAttribute("aria-invalid"));
        Assert.Equal("true", page.Find("#settings-latitude").GetAttribute("aria-invalid"));
        Assert.Contains("Enter a whole zoom level from 0 to 20.", page.Find("#settings-zoom-error").TextContent);
        Assert.Contains("href=\"#settings-latitude\"", page.Find("[role=alert]").InnerHtml);
    }

    [Fact]
    public void StaleSaveAsksForReloadKeepsEditsAndReloadShowsTheOtherBrowsersValues()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.ChangeElsewhere(new SettingsDto("light", 0, 0, 2, "bus", "UTC"));
        page.Find("#settings-theme").Change("dark");
        page.Find("#settings-zoom").Input("7");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Your settings changed", page.Find("[role=alert]").TextContent));
        Assert.Equal("7", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.Equal("dark", page.Find("#settings-theme").GetAttribute("value"));
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.DoesNotContain("Settings saved.", page.Markup);
        Assert.Equal(0, service.AcceptedWrites);

        page.Find("[role=alert] button").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=alert]")));
        Assert.Equal("light", page.Find("#settings-theme").GetAttribute("value"));
        Assert.Equal("bus", page.Find("#settings-transport").GetAttribute("value"));
        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Equal("\"0\"", service.Requests.Single(request => request.Method == HttpMethod.Put).IfMatch);
        Assert.Equal(2, service.Requests.Count(request => request.Method == HttpMethod.Get));
    }

    [Fact]
    public void MissingPreconditionAsksForReloadWithoutClaimingSuccess()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.PreconditionRequired) : null;
        page.Find("#settings-zoom").Input("3");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("must be reloaded before saving", page.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("Settings saved.", page.Markup);
        Assert.Equal("3", page.Find("#settings-zoom").GetAttribute("value"));
    }

    [Fact]
    public void ServiceFieldErrorsAppearBesideTheirInputs()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { errors = new { DefaultMapZoom = new[] { "Zoom must be between 0 and 20." } } }) }
            : null;
        page.Find("#settings-zoom").Input("5");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Zoom must be between 0 and 20.", page.Find("#settings-zoom-error").TextContent));
        Assert.Equal("5", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.DoesNotContain("Settings saved.", page.Markup);
    }

    [Fact]
    public void RejectedSessionStopsWritesOffersSignInAndForgetsPreferences()
    {
        var authentication = Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : null;
        page.Find("#settings-zoom").Input("3");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Your session was rejected", page.Find("[role=alert]").TextContent));
        Assert.Contains("href=\"account\"", page.Find("[role=alert]").InnerHtml);
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Null(Services.GetRequiredService<UserPreferences>().Current);
        Assert.False(authentication.Sessions.IsActive(authentication.User));
        Assert.DoesNotContain("Settings saved.", page.Markup);
    }

    [Fact]
    public void ForbiddenSaveDisablesWritesAndKeepsEdits()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null;
        page.Find("#settings-zoom").Input("4");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("does not allow changing settings", page.Find("[role=alert]").TextContent));
        Assert.Equal("4", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.DoesNotContain("Settings saved.", page.Markup);
    }

    [Fact]
    public void UnavailableServiceKeepsEditsAndAllowsRetry()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        page.Find("#settings-zoom").Input("6");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("temporarily unavailable", page.Find("[role=alert]").TextContent));
        Assert.Equal("6", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.False(page.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.DoesNotContain("Settings saved.", page.Markup);
    }

    [Fact]
    public void NetworkFailureOnSaveDoesNotClaimSuccessAndLeaksNoDiagnostics()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.Override = request => request.Method == HttpMethod.Put ? throw new HttpRequestException("secret transport diagnostic") : null;
        page.Find("#settings-zoom").Input("8");
        page.Find("form.settings-form").Submit();
        page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
        Assert.Equal("8", page.Find("#settings-zoom").GetAttribute("value"));
        Assert.DoesNotContain("Settings saved.", page.Markup);
        Assert.DoesNotContain("secret transport diagnostic", page.Markup);
    }

    [Fact]
    public void FailedLoadOffersRetryThenShowsTheForm()
    {
        Configure();
        service.Override = request => request.Method == HttpMethod.Get ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var page = Render<Settings>();
        page.WaitForAssertion(() => Assert.Contains("temporarily unavailable", page.Find("[role=alert]").TextContent));
        Assert.Empty(page.FindAll("form.settings-form"));
        service.Override = null;
        page.Find("[role=alert] button").Click();
        WaitForForm(page);
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public void LoadingStateIsShownWhileTheSavedValuesArePending()
    {
        // the form-after-load path is covered by every journey above; this only proves the pending state.
        Configure();
        service.ReadGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = Render<Settings>();
        Assert.Contains("Loading your settings", page.Markup);
        Assert.Empty(page.FindAll("form.settings-form"));
        page.Dispose();
        service.ReadGate.SetResult(true);
    }

    [Fact]
    public async Task SavingStateDisablesSaveUntilTheServiceAnswers()
    {
        Configure();
        var page = Render<Settings>();
        WaitForForm(page);
        service.WriteGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Find("#settings-zoom").Input("9");
        var pending = page.Find("form.settings-form").SubmitAsync();
        page.WaitForAssertion(() => Assert.Contains("Saving settings", page.Markup));
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        service.WriteGate.SetResult(true);
        await pending;
        await page.WaitForStateAsync(() => page.Markup.Contains("Settings saved."), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void SignedOutVisitorIsAskedToSignInWithoutAnyRequest()
    {
        Configure(signedIn: false);
        var page = Render<Settings>();
        page.WaitForAssertion(() => Assert.Contains("Sign in to manage your settings", page.Markup));
        Assert.Empty(service.Requests);
        Assert.Empty(page.FindAll("form.settings-form"));
    }

    [Fact]
    public void WithoutWriteGrantValuesAreReadOnly()
    {
        Configure(write: false);
        var page = Render<Settings>();
        WaitForForm(page);
        Assert.Contains("Read-only access", page.Markup);
        Assert.Equal("system", page.Find("#settings-theme").GetAttribute("value"));
        Assert.True(page.Find("fieldset").HasAttribute("disabled"));
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
    }
}
