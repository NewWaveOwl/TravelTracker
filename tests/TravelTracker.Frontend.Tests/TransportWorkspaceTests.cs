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
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Transport;
using TravelTracker.Frontend.Features.Transport;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Isolated frontend authorization and public HTTP contract tests; not real bearer validation. */
public sealed class TransportWorkspaceTests : BunitContext
{
    static readonly TransportDto Train = new(Guid.Parse("00000000-0000-0000-0000-000000000141"), "train", "Saved train", "Reusable rail entry");
    static readonly RoutePointDto Station = new(Guid.Parse("00000000-0000-0000-0000-000000000142"), "transport_stop", "Saved station", "1 Station Road", 43.6m, -79.3m, null);

    /**
     * Wires the actual protected HTTP client to a deterministic service stub.
     * @param handler Handles public service requests.
     * @param write Grants catalogue write for form scenarios.
     * @return A switchable fake validated identity.
     */
    SwitchableAuthentication Configure(StubHandler handler, bool write = true)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-04T10:00:00Z"));
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, write ? "catalogue.read catalogue.write" : "catalogue.read"));
        var client = new ProtectedClient(authentication, new ServiceDirectory(new Dictionary<string, string> { ["Transport"] = "http://localhost:5105" }), new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock); Services.AddSingleton(sessions); Services.AddSingleton<AuthenticationStateProvider>(authentication); Services.AddSingleton(new TransportClient(client));
        return authentication;
    }

    static HttpResponseMessage Read(HttpRequestMessage request)
    {
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        var path = request.RequestUri!.AbsolutePath;
        object result = path.Contains("transport-modes") ? path.EndsWith(Train.TransportModeId.ToString()) ? Train : new[] { Train } : path.EndsWith(Station.RoutePointId.ToString()) ? Station : new[] { Station };
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
    }

    [Fact]
    public void SavedSelectionRetainsPublicIdsAndMapMarkerSelectsDestination()
    {
        Configure(new StubHandler((request, _) => Task.FromResult(Read(request))), false);
        IReadOnlyList<TransportSelection> draft = [];
        var page = Render<TransportWorkspace>(p => p.Add(v => v.SelectionChanged, value => draft = value));
        page.WaitForAssertion(() => Assert.True(page.Markup.Contains("Saved train"), page.Markup));
        page.FindAll("button.transport-card").First().Click();
        page.FindAll("button").Single(b => b.TextContent == "Choose on map").Click();
        page.Find("svg g[role=button]").Click();
        page.WaitForAssertion(() => Assert.Contains("Reusable location; no timetable", page.Markup));
        page.Find(".transport-add").Click();
        page.WaitForAssertion(() => Assert.Single(draft));
        Assert.Equal(Train.TransportModeId, draft[0].Transport.TransportModeId);
        Assert.Equal(Station.RoutePointId, draft[0].Destination.RoutePointId);
        Assert.Contains("Travel time not supplied", page.Markup);
        Assert.DoesNotContain("Create destination location", page.Markup);
    }

    [Fact]
    public void ForbiddenCreateKeepsDraftAndDoesNotAddUnsavedSelection()
    {
        var posts = 0;
        Configure(new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Post) { posts++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }
            return Task.FromResult(Read(request));
        }));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.True(page.Markup.Contains("Saved train"), page.Markup));
        page.Find("#new-point-name").Change("Pending location"); page.Find("#new-point-lat").Change("45"); page.Find("#new-point-lon").Change("-73");
        page.Find(".point-create").Submit();
        page.WaitForAssertion(() => Assert.Contains("permission", page.Find("[role=alert]").TextContent));
        Assert.Equal("Pending location", page.Find("#new-point-name").GetAttribute("value"));
        Assert.Empty(page.FindAll(".transport-chain li")); Assert.Equal(1, posts);
    }

    [Fact]
    public void InvalidCoordinatesNeverWriteAndKeepPendingFields()
    {
        var posts = 0;
        Configure(new StubHandler((request, _) => { if (request.Method == HttpMethod.Post) posts++; return Task.FromResult(Read(request)); }));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.True(page.Markup.Contains("Saved train"), page.Markup));
        page.Find("#new-point-name").Change("Draft"); page.Find("#new-point-lat").Change("91"); page.Find("#new-point-lon").Change("0"); page.Find(".point-create").Submit();
        Assert.Contains("valid latitude", page.Find("[role=alert]").TextContent); Assert.Equal(0, posts);
        Assert.Equal("91", page.Find("#new-point-lat").GetAttribute("value"));
    }

    [Fact]
    public void UnavailableCatalogueCanRetryWithoutLosingSearch()
    {
        var unavailable = true;
        Configure(new StubHandler((request, _) => Task.FromResult(unavailable ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Read(request))));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("unavailable", page.Markup));
        page.Find("#transport-query").Change("rail"); unavailable = false;
        page.Find("[role=alert] button").Click();
        page.WaitForAssertion(() => Assert.True(page.Markup.Contains("Saved train"), page.Markup));
        Assert.Equal("rail", page.Find("#transport-query").GetAttribute("value"));
    }

    [Fact]
    public void CreateUsesOnlyApprovedFieldsAndReturnedSavedIdentity()
    {
        Dictionary<string, JsonElement>? fields = null;
        Configure(new StubHandler(async (request, _) =>
        {
            if (request.Method != HttpMethod.Post) return Read(request);
            fields = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(Station) };
        }));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.True(page.Markup.Contains("Saved train"), page.Markup));
        page.Find("#new-point-name").Change("Saved station"); page.Find("#new-point-lat").Change("43.6"); page.Find("#new-point-lon").Change("-79.3"); page.Find(".point-create").Submit();
        page.WaitForAssertion(() => Assert.Contains("Destination location saved.", page.Markup));
        Assert.NotNull(fields); Assert.DoesNotContain("routePointId", fields.Keys); Assert.DoesNotContain("departure", fields.Keys);
        Assert.Equal(43.6m, fields["lat"].GetDecimal());
    }

    [Fact]
    public void RepeatedSavedSelectionsAreSeparateOccurrencesAndSignOutClearsParent()
    {
        var authentication = Configure(new StubHandler((request, _) => Task.FromResult(Read(request))), false);
        IReadOnlyList<TransportSelection> draft = [];
        var page = Render<TransportWorkspace>(p => p.Add(v => v.SelectionChanged, value => draft = value));
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        for (var i = 0; i < 2; i++)
        {
            page.FindAll(".transport-columns > section")[0].QuerySelector("button.transport-card")!.Click();
            page.FindAll(".transport-columns > section")[1].QuerySelector("button.transport-card")!.Click();
            page.Find(".transport-add").Click();
        }
        page.WaitForAssertion(() => Assert.Equal(2, draft.Count));
        Assert.NotEqual(draft[0].OccurrenceId, draft[1].OccurrenceId);
        authentication.SignOut();
        page.WaitForAssertion(() => Assert.Empty(draft));
        Assert.DoesNotContain("Saved station", page.Markup);
    }

    [Fact]
    public void RejectedTokenClearsPreviouslyPublishedParentDraft()
    {
        var reject = false;
        Configure(new StubHandler((request, _) => Task.FromResult(reject ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : Read(request))), false);
        IReadOnlyList<TransportSelection> draft = [];
        var page = Render<TransportWorkspace>(p => p.Add(v => v.SelectionChanged, value => draft = value));
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        page.FindAll("button.transport-card")[0].Click(); page.FindAll("button.transport-card")[1].Click(); page.Find(".transport-add").Click();
        page.WaitForAssertion(() => Assert.Single(draft)); reject = true;
        page.FindAll(".transport-columns > section")[0].QuerySelector("form")!.Submit();
        page.WaitForAssertion(() => Assert.Empty(draft));
        Assert.Contains("Sign in", page.Markup);
    }

    [Fact]
    public void ServerValidationIsLinkedToTheRejectedFormField()
    {
        Configure(new StubHandler((request, _) => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { errors = new { sourceId = new[] { "Source identifier is invalid." } } }) }
            : Read(request))));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        page.Find("#new-point-name").Change("Point"); page.Find("#new-point-lat").Change("0"); page.Find("#new-point-lon").Change("0"); page.Find(".point-create").Submit();
        page.WaitForAssertion(() => Assert.Equal("Source identifier is invalid.", page.Find("#new-point-source-error").TextContent));
        Assert.Equal("new-point-source-error", page.Find("#new-point-source").GetAttribute("aria-describedby"));
    }

    [Fact]
    public async Task SignOutDuringConfirmationCannotPublishTheOldSelection()
    {
        var delayed = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirm = false;
        var authentication = Configure(new StubHandler((request, _) =>
        {
            if (confirm && request.RequestUri!.AbsolutePath.EndsWith(Station.RoutePointId.ToString())) { started.TrySetResult(); return delayed.Task; }
            return Task.FromResult(Read(request));
        }));
        IReadOnlyList<TransportSelection> draft = [];
        var page = Render<TransportWorkspace>(p => p.Add(v => v.SelectionChanged, value => draft = value));
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        page.FindAll("button.transport-card")[0].Click(); page.FindAll("button.transport-card")[1].Click();
        confirm = true;
        var click = page.Find(".transport-add").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        authentication.SignOut();
        page.WaitForAssertion(() => Assert.Contains("Sign in", page.Markup));
        delayed.SetResult(new(HttpStatusCode.OK) { Content = JsonContent.Create(Station) });
        await click;
        Assert.Empty(draft); Assert.DoesNotContain("Saved station", page.Markup);
    }

    [Fact]
    public void EmptyResultsKeepSearchAndMapControlsUsable()
    {
        Configure(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) })), false);
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No locations found", page.Markup));
        Assert.Contains("No transport found", page.Markup);
        Assert.False(page.Find("#transport-query").HasAttribute("disabled"));
        Assert.False(page.Find("#point-query").HasAttribute("disabled"));
        page.FindAll("button").Single(b => b.TextContent == "Choose on map").Click();
        Assert.Empty(page.FindAll("svg g[role=button]"));
        Assert.True(page.Find(".transport-add").HasAttribute("disabled"));
    }

    [Fact]
    public void ConnectionFailureDuringSaveRetainsPendingInputWithoutAddingDraftEntry()
    {
        Configure(new StubHandler((request, _) => request.Method == HttpMethod.Post
            ? throw new HttpRequestException("Offline")
            : Task.FromResult(Read(request))));
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        page.Find("#new-transport-name").Change("Pending train"); page.Find("#new-transport-code").Change("train"); page.Find(".transport-create").Submit();
        page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
        Assert.Equal("Pending train", page.Find("#new-transport-name").GetAttribute("value"));
        Assert.Equal("train", page.Find("#new-transport-code").GetAttribute("value"));
        Assert.Empty(page.FindAll(".transport-chain li"));
    }

    [Fact]
    public void FiltersSendEncodedNamesAndExactSupportedCodeAndType()
    {
        var requests = new List<string>();
        Configure(new StubHandler((request, _) => { requests.Add(request.RequestUri!.OriginalString); return Task.FromResult(Read(request)); }), false);
        var page = Render<TransportWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("Saved train", page.Markup));
        Assert.Equal(7, page.FindAll("#transport-code option").Count);
        Assert.Equal(4, page.FindAll("#point-type option").Count);
        page.Find("#transport-query").Change("rail & ferry"); page.Find("#transport-code").Change("bike");
        page.Find("#point-query").Change("museum/park"); page.Find("#point-type").Change("attraction");
        page.FindAll(".transport-columns > section")[0].QuerySelector("form")!.Submit();
        page.WaitForAssertion(() => Assert.Contains(requests, url => url.EndsWith("/api/v1/transport-modes?name=rail%20%26%20ferry&code=bike", StringComparison.Ordinal)));
        Assert.Contains(requests, url => url.EndsWith("/api/v1/route-points?name=museum%2Fpark&type=attraction", StringComparison.Ordinal));
    }

    [Fact]
    public void EditAndRemoveChainPublishUpdatedSavedPublicIds()
    {
        var bus = new TransportDto(Guid.Parse("00000000-0000-0000-0000-000000000143"), "bus", "Saved bus", null);
        Configure(new StubHandler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("transport-modes", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { Train, bus }) });
            if (path.EndsWith(bus.TransportModeId.ToString(), StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(bus) });
            return Task.FromResult(Read(request));
        }), false);
        IReadOnlyList<TransportSelection> draft = [];
        var page = Render<TransportWorkspace>(p => p.Add(v => v.SelectionChanged, value => draft = value));
        page.WaitForAssertion(() => Assert.Contains("Saved bus", page.Markup));
        page.FindAll(".transport-columns > section")[0].QuerySelector("button.transport-card")!.Click();
        page.FindAll(".transport-columns > section")[1].QuerySelector("button.transport-card")!.Click();
        page.Find(".transport-add").Click(); page.WaitForAssertion(() => Assert.Single(draft));
        Assert.Equal(Train.TransportModeId, draft[0].Transport.TransportModeId);
        page.Find(".transport-chain button.transport-card").Click();
        page.FindAll(".transport-columns > section")[0].QuerySelectorAll("button.transport-card")[1].Click();
        page.Find(".transport-add").Click();
        page.WaitForAssertion(() => Assert.Equal(bus.TransportModeId, draft.Single().Transport.TransportModeId));
        Assert.Equal(Station.RoutePointId, draft[0].Destination.RoutePointId);
        page.Find(".chain-remove").Click(); page.WaitForAssertion(() => Assert.Empty(draft));
        Assert.Empty(page.FindAll(".transport-chain li"));
    }
}
