using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Components.Layout;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** UI authorization tests use fake validated identities; these do not establish bearer validation. */
public sealed class AttractionWorkspaceTests : BunitContext
{
    static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000123");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));

    /**
     * Connects the real frontend HTTP stack to an isolated deterministic HTTP stub.
     * @param handler Answers public contract calls without an external service.
     * @param write Whether the fake signed-in grant includes catalogue.write.
     * @return The switchable authenticated session for sign-out scenarios.
     */
    SwitchableAuthentication Configure(StubHandler handler, bool write = true)
    {
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, write ? "catalogue.read catalogue.write" : "catalogue.read"));
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Attractions"] = "http://localhost:5104" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new AttractionsClient(client));
        Services.AddLogging();
        return authentication;
    }

    [Fact]
    public void CreateSearchEditAndReloadKeepsSavedContractValues()
    {
        AttractionDto? stored = null;
        Dictionary<string, JsonElement>? patch = null;
        var handler = new StubHandler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var fields = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                Assert.DoesNotContain("approximationMinutes", fields!.Keys);
                stored = new(Id, fields["name"].GetString()!, fields["description"].GetString(), fields["rating"].GetDecimal(), fields["address"].GetString()!, fields["lat"].GetDecimal(), fields["lon"].GetDecimal(), fields["estimatedVisitMinutes"].GetInt32());
                return new(HttpStatusCode.Created) { Content = JsonContent.Create(stored) };
            }
            if (request.Method == HttpMethod.Patch)
            {
                patch = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                stored = stored! with { Description = null, Rating = null };
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(stored) };
            }
            return new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(stored) : JsonContent.Create(stored is null ? Array.Empty<AttractionDto>() : new[] { stored }) };
        });
        Configure(handler);
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("Montreal Museum");
        page.Find("#attraction-address").Input("Montreal");
        page.Find("#attraction-lat").Input("45.48");
        page.Find("#attraction-lon").Input("-73.57");
        page.Find("#attraction-estimatedVisitMinutes").Input("120");
        page.Find("#attraction-approximationMinutes").Input("30");
        page.Find("#attraction-rating").Input("4");
        page.Find("#attraction-description").Change("Original description");
        page.Find(".attraction-editor form").Submit();
        page.WaitForAssertion(() => Assert.Contains("Attraction saved.", page.Markup));
        page.Find("#attraction-search").Change("Montreal");
        page.Find(".attraction-search").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".attraction-card")));
        page.Find(".attraction-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Original description", page.Markup));
        page.Find(".attraction-editor button").Click();
        page.Find("#attraction-rating").Input("");
        page.Find("#attraction-description").Change("");
        page.Find(".attraction-editor form").Submit();
        page.WaitForAssertion(() => Assert.Contains("No description yet.", page.Markup));
        Assert.Equal(2, patch!.Count);
        Assert.Equal(JsonValueKind.Null, patch["rating"].ValueKind);
        Assert.Equal(JsonValueKind.Null, patch["description"].ValueKind);
        page.Dispose();
        var reloaded = Render<Attractions>();
        reloaded.WaitForAssertion(() => Assert.Single(reloaded.FindAll(".attraction-card")));
        reloaded.Find(".attraction-card button").Click();
        reloaded.WaitForAssertion(() => Assert.Contains("No description yet.", reloaded.Markup));
        Assert.Contains("No rating", reloaded.Markup);
    }

    [Fact]
    public void ReadOnlyGrantCanBrowseButCannotOpenWriteForms()
    {
        var dto = new AttractionDto(Id, "Museum", null, null, "Montreal", 45, -73, 60);
        Configure(new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(dto) : JsonContent.Create(new[] { dto }) })), false);
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".attraction-card")));
        page.Find(".attraction-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Read-only access", page.Markup));
        Assert.Empty(page.FindAll(".attractions-heading button"));
        Assert.Empty(page.FindAll(".attraction-editor button"));
    }

    [Fact]
    public void ValidationPreservesEntriesAndDoesNotSendWrites()
    {
        var writes = 0;
        Configure(new StubHandler((request, _) => { if (request.Method != HttpMethod.Get) writes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) }); }));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("My typed attraction");
        page.Find("#attraction-lat").Input("999");
        page.Find(".attraction-editor form").Submit();
        Assert.Equal(0, writes);
        Assert.Equal("My typed attraction", page.Find("#attraction-name").GetAttribute("value"));
        Assert.Equal("true", page.Find("#attraction-lat").GetAttribute("aria-invalid"));
        Assert.Contains("Check your details", page.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void LocationCancelDiscardsCoordinatesWhileUseAppliesThem()
    {
        Configure(StubHandler.Status(200, "[]"));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("Unsaved museum");
        page.Find("#attraction-lat").Input("45");
        page.Find("#attraction-lon").Input("-73");
        page.Find(".attraction-map-action button").Click();
        Assert.Contains("placeholder", page.Find(".location-picker").TextContent);
        page.Find("#map-lat").Input("46");
        page.Find(".selected-location button[type=button]").Click();
        Assert.Equal("45", page.Find("#attraction-lat").GetAttribute("value"));
        page.Find(".attraction-map-action button").Click();
        page.Find("#map-lat").Input("46");
        page.Find(".selected-location").Submit();
        Assert.Equal("46", page.Find("#attraction-lat").GetAttribute("value"));
        Assert.Equal("Unsaved museum", page.Find("#attraction-name").GetAttribute("value"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(503)]
    public void RejectedSavesRetainInputsAndNeverAnnounceSuccess(int status)
    {
        Configure(new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Get
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) }
            : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("""{"errors":{"name":["Check the attraction name."]}}""") })));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        FillValidDraft(page);
        page.Find(".attraction-editor form").Submit();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[role=alert]")));
        Assert.Equal("Montreal Museum", page.Find("#attraction-name").GetAttribute("value"));
        Assert.Equal("30", page.Find("#attraction-approximationMinutes").GetAttribute("value"));
        Assert.DoesNotContain("Attraction saved.", page.Markup);
        if (status == 403) Assert.True(page.Find("fieldset").HasAttribute("disabled"));
    }

    [Fact]
    public async Task PreventsDuplicateSubmissionsWhileSaving()
    {
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) });
            Interlocked.Increment(ref writes);
            return completion.Task;
        }));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        FillValidDraft(page);
        var first = page.Find(".attraction-editor form").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, writes));
        await page.Find(".attraction-editor form").SubmitAsync();
        Assert.True(page.Find(".attraction-editor button[type=submit]").HasAttribute("disabled"));
        Assert.Equal(1, writes);
        completion.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        await first;
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[role=alert]")));
        Assert.Equal("Montreal Museum", page.Find("#attraction-name").GetAttribute("value"));
    }

    [Fact]
    public void ShowsTwoColumnCatalogueAndExportsVisualEvidenceWhenRequested()
    {
        var names = new[] { "Aga Khan Museum", "Art Gallery of Ontario", "Casa Loma", "Royal Ontario Museum" };
        Configure(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(names.Select((name, index) => new AttractionDto(Guid.Parse($"00000000-0000-0000-0000-00000000000{index}"), name, null, 4.7m, "Montreal", 45.48m, -73.57m, 120)).ToArray()),
        })));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Equal(4, page.FindAll(".attraction-card").Count));
        page.Find(".attractions-heading button").Click();
        FillValidDraft(page);
        ExportVisual(page, "attraction-workspace.html");
        page.Find(".attraction-map-action button").Click();
        ExportVisual(page, "attraction-location.html");
        Assert.NotNull(page.Find(".location-picker .map-preview img"));
    }

    static void FillValidDraft(IRenderedComponent<Attractions> page)
    {
        page.Find("#attraction-name").Input("Montreal Museum");
        page.Find("#attraction-address").Input("Montreal");
        page.Find("#attraction-lat").Input("45.48");
        page.Find("#attraction-lon").Input("-73.57");
        page.Find("#attraction-estimatedVisitMinutes").Input("120");
        page.Find("#attraction-approximationMinutes").Input("30");
        page.Find("#attraction-rating").Input("4.7");
    }

    /**
     * Exports optional rendered test evidence for browser-based layout review against the design.
     * @param page The tested workspace state.
     * @param fileName The evidence file name within the explicitly configured output folder.
     */
    void ExportVisual(IRenderedComponent<Attractions> page, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("TRAVELTRACKER_VISUAL_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var header = Render<SiteHeader>();
        File.WriteAllText(Path.Combine(directory, fileName), $"<!doctype html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1'><base href='/'><link rel='stylesheet' href='/fonts.css'><link rel='stylesheet' href='/styles.css'></head><body>{header.Markup}<main id='main'>{page.Markup}</main></body></html>");
    }
}
