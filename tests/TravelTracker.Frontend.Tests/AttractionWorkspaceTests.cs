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
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** UI authorization tests use fake validated identities; these do not establish bearer validation. */
public sealed class AttractionWorkspaceTests : BunitContext
{
    static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000123");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));

    [Fact]
    public void AttractionDialogKeepsCatalogueInertAndEscapeCancelsWithoutWriting()
    {
        var writes = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) });
        }));
        var page = Render<Attractions>();
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("Unconfirmed museum");

        Assert.True(page.Find(".attraction-catalogue").HasAttribute("inert"));
        page.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(page.FindAll("[role=dialog]"));
        Assert.False(page.Find(".attraction-catalogue").HasAttribute("inert"));
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task BusyAttractionDialogKeepsFocusInsideAndCannotBeDismissedDuringSave()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure(new StubHandler((request, _) => request.Method == HttpMethod.Post ? response.Task
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) })));
        var page = Render<Attractions>();
        page.Find(".attractions-heading button").Click();
        var headingFocus = JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal)).Arguments[0];
        page.Find("#attraction-name").Input("Museum");
        page.Find("#attraction-lat").Input("45");
        page.Find("#attraction-lon").Input("-73");
        page.Find("#attraction-estimatedVisitMinutes").Input("60");
        var submission = page.Find(".attraction-editor form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find(".attraction-editor fieldset").HasAttribute("disabled")));

        page.Find("[role=dialog] > span[tabindex='0']").Focus();
        Assert.Equal(headingFocus, JSInterop.Invocations.Last(invocation => invocation.Identifier.EndsWith("focus", StringComparison.Ordinal)).Arguments[0]);
        page.Find("[role=dialog]").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Single(page.FindAll("[role=dialog]"));

        response.SetResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new AttractionDto(Id, "Museum", null, null, "", 45, -73, 60)) });
        await submission;
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=dialog]")));
    }

    /**
     * connects the real frontend HTTP stack to an isolated deterministic HTTP stub.
     * @param handler Answers public contract calls without an external service.
     * @param write Whether the fake signed-in grant includes catalogue.write.
     * @return The switchable authenticated session for sign-out scenarios.
     */
    SwitchableAuthentication Configure(StubHandler handler, bool write = true)
    {
        JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js").Mode = JSRuntimeMode.Loose;
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10), write ? "catalogue.read catalogue.write" : "catalogue.read");
        var client = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string> { ["Attractions"] = "http://localhost:5104" }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new AttractionsClient(client));
        Services.AddSingleton(new TravelTracker.Frontend.Api.Users.UsersClient(client, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()));
        Services.AddScoped<TravelTracker.Frontend.Services.AdminAccessState>();
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
        var attractionDto = new AttractionDto(Id, "Museum", null, null, "Montreal", 45, -73, 60);
        Configure(new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(attractionDto) : JsonContent.Create(new[] { attractionDto }) })), false);
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".attraction-card")));
        page.Find(".attraction-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Read-only access", page.Markup));
        Assert.Empty(page.FindAll(".attractions-heading button"));
        Assert.Empty(page.FindAll(".attraction-editor button"));
    }

    [Fact]
    public async Task ValidationPreservesEntriesAndDoesNotSendWrites()
    {
        var writes = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) });
        }));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("My typed attraction");
        page.Find("#attraction-lat").Input("999");
        await page.Find(".attraction-editor form").SubmitAsync();
        Assert.Equal(0, writes);
        Assert.Equal("My typed attraction", page.Find("#attraction-name").GetAttribute("value"));
        Assert.Equal("true", page.Find("#attraction-lat").GetAttribute("aria-invalid"));
        Assert.Contains("Check your details", page.Find("[role=alert]").TextContent);
    }

    [Fact]
    public async Task LocationCancelDiscardsCoordinatesWhileUseAppliesThem()
    {
        Configure(StubHandler.Status(200, "[]"));
        var page = Render<Attractions>();
        page.WaitForAssertion(() => Assert.Contains("No attractions found", page.Markup));
        page.Find(".attractions-heading button").Click();
        page.Find("#attraction-name").Input("Unsaved museum");
        page.Find("#attraction-address").Input("123 Museum Street");
        page.Find("#attraction-lat").Input("45");
        page.Find("#attraction-lon").Input("-73");
        page.Find(".attraction-map-action button").Click();
        Assert.Contains("initial map view does not select", page.Find(".location-picker").TextContent);
        Assert.Equal("123 Museum Street", page.Find(".selected-location .address-search input").GetAttribute("value"));
        await page.InvokeAsync(() => page.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((46m, -74m)));
        page.Find(".selected-location .location-actions button.secondary").Click();
        Assert.Equal("45", page.Find("#attraction-lat").GetAttribute("value"));
        Assert.Equal("-73", page.Find("#attraction-lon").GetAttribute("value"));
        Assert.Equal("123 Museum Street", page.Find("#attraction-address").GetAttribute("value"));
        page.Find(".attraction-map-action button").Click();
        await page.InvokeAsync(() => page.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((46m, -74m)));
        page.Find(".selected-location .address-search input").Input("123 Museum Street");
        page.Find(".selected-location .location-actions button:not(.secondary)").Click();
        Assert.Equal("46", page.Find("#attraction-lat").GetAttribute("value"));
        Assert.Equal("-74", page.Find("#attraction-lon").GetAttribute("value"));
        Assert.Equal("123 Museum Street", page.Find("#attraction-address").GetAttribute("value"));
        Assert.Equal("Unsaved museum", page.Find("#attraction-name").GetAttribute("value"));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(503)]
    public void RejectedSavesRetainInputsAndNeverAnnounceSuccess(int status)
    {
        var page = RenderRejectedSave(status);

        Assert.Equal("Montreal Museum", page.Find("#attraction-name").GetAttribute("value"));
        Assert.Equal("30", page.Find("#attraction-approximationMinutes").GetAttribute("value"));
        Assert.DoesNotContain("Attraction saved.", page.Markup);
    }

    [Fact]
    public void ForbiddenSaveDisablesTheEditorFieldset()
    {
        var page = RenderRejectedSave(403);

        Assert.True(page.Find("fieldset").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForbiddenReadClearsCachedCardsAndPrivateDetails(bool details)
    {
        var saved = new AttractionDto(Id, "Private museum", "Private description", null, "Private address", 45, -73, 30);
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(saved) : JsonContent.Create(new[] { saved })
        }));
        Configure(handler);
        var page = Render<Attractions>();
        page.Find(".attraction-card button").Click();
        Assert.Contains("Private description", page.Markup);

        handler.RespondWith(403);
        if (details) page.Find(".attraction-card button").Click();
        else page.Find(".attraction-search").Submit();

        Assert.Single(page.FindAll("[role=alert]"));
        Assert.Empty(page.FindAll(".attraction-card"));
        Assert.DoesNotContain("Private description", page.Markup);
        Assert.DoesNotContain("Private address", page.Markup);
        Assert.DoesNotContain("Edit attraction", page.Markup);
        Assert.DoesNotContain("Sign in to browse", page.Markup);
    }

    [Fact]
    public void MissingDetailRemovesOnlyTheUnavailableCardAndItsEditableSelection()
    {
        var saved = new AttractionDto(Id, "Unavailable museum", "Private description", null, "Private address", 45, -73, 30);
        var other = saved with { AttractionId = Guid.Parse("00000000-0000-0000-0000-000000000124"), Name = "Other museum", Description = "Other description", Address = "Other address" };
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(saved) : JsonContent.Create(new[] { saved, other })
        }));
        Configure(handler);
        var page = Render<Attractions>();
        page.Find(".attraction-card button").Click();
        handler.RespondWith(404);
        page.Find(".attraction-card button").Click();

        Assert.Single(page.FindAll("[role=alert]"));
        Assert.Single(page.FindAll(".attraction-card"));
        Assert.Contains("Other museum", page.Markup);
        Assert.DoesNotContain("Unavailable museum", page.Markup);
        Assert.DoesNotContain("Private description", page.Markup);
        Assert.DoesNotContain("Edit attraction", page.Markup);
    }

    [Fact]
    public void ForbiddenEditPreservesPendingFieldsWithoutClaimingSaveSuccess()
    {
        var saved = new AttractionDto(Id, "Museum", "Original description", null, "Address", 45, -73, 30);
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(saved) : JsonContent.Create(new[] { saved })
        }));
        Configure(handler);
        var page = Render<Attractions>();
        page.Find(".attraction-card button").Click();
        page.FindAll("button").Single(button => button.TextContent == "Edit attraction").Click();
        page.Find("#attraction-name").Input("Pending museum name");
        handler.RespondWith(403);
        page.Find(".attraction-editor form").Submit();

        Assert.Equal("Pending museum name", page.Find("#attraction-name").GetAttribute("value"));
        Assert.True(page.Find("fieldset").HasAttribute("disabled"));
        Assert.DoesNotContain("Attraction saved.", page.Markup);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(503)]
    public void FailedReadDistinguishesRejectedSessionFromTemporaryServiceFailure(int status)
    {
        var saved = new AttractionDto(Id, "Cached museum", "Cached description", null, "Address", 45, -73, 30);
        var handler = new StubHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith(Id.ToString()) ? JsonContent.Create(saved) : JsonContent.Create(new[] { saved })
        }));
        Configure(handler);
        var page = Render<Attractions>();
        page.Find(".attraction-card button").Click();
        handler.RespondWith(status);
        page.Find(".attraction-search").Submit();

        if (status == 401)
        {
            Assert.Contains("Sign in to browse", page.Markup);
            Assert.DoesNotContain("Cached description", page.Markup);
            Assert.Empty(page.FindAll(".attraction-card"));
        }
        else
        {
            Assert.Contains("Cached description", page.Markup);
            Assert.Single(page.FindAll(".attraction-card"));
            Assert.Contains("Retry search", page.Markup);
            Assert.DoesNotContain("Sign in to browse", page.Markup);
        }
    }

    /**
     * Opens the create form, fills it with valid values and submits it to a service that rejects the write with the given status.
     * @param status The status the write answers with.
     * @return The rendered page after the rejection has been shown.
     */
    IRenderedComponent<Attractions> RenderRejectedSave(int status)
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
        return page;
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
        Assert.NotNull(page.Find(".location-picker .trip-map-canvas svg"));
        Assert.NotNull(page.Find(".location-picker button[aria-label='Pick map center']"));
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
     * exports optional rendered test evidence for browser-based layout review against the design.
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
