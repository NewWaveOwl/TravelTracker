using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Tests.Support;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Tests;

/** verifies the shared draft and the Trips HTTP contract through rendered planner controls. */
public sealed class TripPlannerTests : BunitContext
{
    static readonly Guid TripId = Guid.Parse("a4fc96df-c35b-4305-92d8-4ae86638b585");
    static readonly TripPlaceDto Hotel = new(Guid.Parse("00000000-0000-0000-0000-000000009001"), "accommodation", "Hotel", "", 45.50m, -73.57m);
    static readonly TripPlaceDto Museum = new(Guid.Parse("00000000-0000-0000-0000-000000009002"), "attraction", "Museum", "", 45.51m, -73.58m);
    static readonly TripDto.Mode Train = new(Guid.Parse("00000000-0000-0000-0000-000000009003"), "train", "Train");
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-14T09:00:00-05:00");
    static readonly TripDto Saved = new(TripId, "Weekend", "America/Toronto", Start, Start.AddHours(4),
        false, [new(1, Hotel, Museum, Train, Start.AddHours(1), Start.AddHours(2))]);

    [Theory]
    [InlineData("Save trip")]
    [InlineData("Review trip")]
    public void GlobalActionsRequestTheDetailsPanelEvenWhenItsContentWasCollapsed(string action)
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var details = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last();
        var originalVersion = details.Instance.RevealVersion;
        page.Find(".map-trip-name").Change("");

        Click(page, action);

        page.WaitForAssertion(() => Assert.True(details.Instance.RevealVersion > originalVersion));
        Assert.Contains("Enter a trip name", page.Find("[role=alert]").TextContent);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("trip")]
    [InlineData("new-trip")]
    public void WorkspaceSaveIsAvailableBeforeReviewAndCreatesAnEmptyTrip(string route)
    {
        SaveTripRequest? created = null;
        Configure(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(Saved with { Name = created!.Name, Legs = [], Stops = [] });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/{route}");
        var page = Render<NewTrip>();
        var save = page.Find("[data-trip-save]");
        Assert.Equal("Control+S Meta+S", save.GetAttribute("aria-keyshortcuts"));
        Assert.NotNull(save.Closest(".trip-workspace-controls"));
        Assert.Null(save.Closest("#trip-tools-panel"));
        Assert.Empty(page.FindAll(".trip-review"));
        var nameField = page.Find(".map-trip-name, .trip-basics input");
        Assert.Equal("200", nameField.GetAttribute("maxlength"));
        nameField.Change("An empty plan");
        SelectTripDates(page, new(2026, 11, 14), new(2026, 11, 14));

        save.Click();

        page.WaitForAssertion(() => Assert.NotNull(created));
        Assert.Equal("An empty plan", created!.Name);
        Assert.Empty(created.Legs);
        Assert.Empty(created.Stops!);
        Assert.Contains("Saved An empty plan", page.Find(".trip-workspace-controls").TextContent);
        Click(page, "Back to editor");
        page.Find(".map-trip-name, .trip-basics input").Change("Changed after saving");
        Assert.DoesNotContain("Saved An empty plan", page.Find(".trip-workspace-controls").TextContent);
    }


    [Fact]
    public void WorkspaceSaveValidatesMissingBasicsWithoutHidingTheAction()
    {
        Configure((_, _) => throw new InvalidOperationException("Invalid drafts must not send HTTP requests."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();

        page.Find("[data-trip-save]").Click();

        Assert.Contains("name", page.Find(".trip-workspace-controls [role=alert]").TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Save trip", page.Find("[data-trip-save]").TextContent);
        Assert.False(page.Find("[data-trip-save]").HasAttribute("disabled"));
    }


    [Fact]
    public async Task WorkspaceSaveStaysVisibleWithClosedToolsAndRetainsDraftAfterFailedSingleStopSave()
    {
        var single = Saved with { Legs = [], Stops = [Museum] };
        var writes = new List<SaveTripRequest>();
        var saving = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fail = true;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(single);
            writes.Add((await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!);
            await saving.Task.WaitAsync(token);
            return fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(single with { Name = writes[^1].Name });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find(".map-trip-name").Change("Keep my single stop");
        var detailsPanel = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last();
        detailsPanel.Find(".movable-panel-body").SetAttribute("inert", "");
        var save = page.Find("[data-trip-save]");

        var pendingSave = save.ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Single(writes));
        Assert.Equal("Saving trip", page.Find("[data-trip-save]").TextContent);
        Assert.Contains("Saving trip", page.Find(".trip-workspace-controls [role=status]").TextContent);
        Assert.True(page.Find("[data-trip-save]").HasAttribute("disabled"));
        save.Click();
        Assert.Single(writes);
        saving.SetResult(true);
        await pendingSave;

        page.WaitForAssertion(() => Assert.Contains("Your draft is still here", page.Find(".trip-workspace-controls [role=alert]").TextContent));
        Assert.Equal("Keep my single stop", page.Find(".map-trip-name").GetAttribute("value"));
        Assert.Equal(Museum, Assert.Single(page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>().Instance.Stops));
        Assert.Empty(Assert.Single(writes).Legs);
        Assert.Single(writes[0].Stops!);
        fail = false;
        page.Find("[data-trip-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Saved Keep my single stop", page.Find(".trip-workspace-controls").TextContent));
        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Name, writes[1].Name);
        Assert.Equal(writes[0].Stops, writes[1].Stops);
    }


    [Fact]
    public async Task WorkspaceSaveRevealsIncompleteMarkerDetailsInsteadOfSavingOldValues()
    {
        var writes = 0;
        Configure((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Json(Saved));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("   ");
        var detailsPanel = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last();
        var revealBeforeSave = detailsPanel.Instance.RevealVersion;
        detailsPanel.Find(".movable-panel-body").SetAttribute("inert", "");

        page.Find("[data-trip-save]").Click();

        Assert.Equal(0, writes);
        Assert.Contains("Enter a place name", page.Find("[role=alert]").TextContent);
        Assert.True(detailsPanel.Instance.RevealVersion > revealBeforeSave);
        Assert.Equal("   ", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
    }


    [Fact]
    public async Task ReviewShowsPendingLocationsWithoutWritingThem()
    {
        var writes = 0;
        Configure((request, _) =>
        {
            if (request.Method != HttpMethod.Get) writes++;
            return Task.FromResult(Json(Saved));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MoveMarker(1, 15, 10));

        Click(page, "Review trip");

        Assert.Equal(0, writes);
        Assert.Single(page.FindAll(".trip-review"));
        Assert.Contains("Saving this trip also saves new locations", page.Find(".trip-review").TextContent);
        Assert.Single(page.FindAll("[data-trip-save]"));
        Assert.Equal(Guid.Empty, canvas.Instance.Stops[1].PlaceId);
    }


    [Fact]
    public async Task WorkspaceShortcutsAreRegisteredOnceAndDisconnectedWhenEditorCloses()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        var module = JSInterop.SetupModule("./Components/Pages/NewTrip.razor.js");
        var shortcuts = module.SetupModule("watchEditorShortcuts", _ => true);
        shortcuts.SetupVoid("disconnect").SetVoidResult();
        var page = Render<NewTrip>();
        page.Find(".trip-basics input").Change("Still editing");
        Assert.Single(module.Invocations["watchEditorShortcuts"]);

        await page.Instance.DisposeAsync();

        Assert.Single(shortcuts.Invocations["disconnect"]);
    }


    [Fact]
    public void MapPlannerCreatesAnEmptyTripAndReopensItWithNoStops()
    {
        SaveTripRequest? created = null;
        var empty = Saved with { Legs = [], Stops = [] };
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(empty);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/trips", request.RequestUri!.AbsolutePath);
            created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(empty with { Name = created!.Name });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/new-trip");
        var page = Render<NewTrip>();
        page.Find(".trip-basics input").Change("Empty ideas");
        SelectTripDates(page, new(2026, 11, 14), new(2026, 11, 14));
        Click(page, "Review trip");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.NotNull(created));
        Assert.Empty(created!.Legs);
        Assert.Empty(created.Stops!);
        Assert.Empty(page.FindAll(".trip-stop"));
        Assert.Contains("Saved Empty ideas", page.Markup);

        var reopened = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.Empty(reopened.FindAll(".trip-stop"));
    }


    [Theory]
    [InlineData("accommodation", false)]
    [InlineData("attraction", true)]
    [InlineData("transport_stop", true)]
    public void SingleStopSavesWithoutAConnectionAndReopensItsSnapshot(string type, bool roundTrip)
    {
        var stop = Hotel with { Type = type, Name = "Only place" };
        var single = Saved with { IsRoundTrip = roundTrip, Legs = [], Stops = [stop] };
        SaveTripRequest? replacement = null;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(single);
            Assert.Equal(HttpMethod.Put, request.Method);
            replacement = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(single with { Name = replacement!.Name }, "\"2\"");
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.Equal("Only place", Assert.Single(page.FindAll(".trip-stop strong")).TextContent);
        Click(page, "Review trip");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.NotNull(replacement));
        Assert.Empty(replacement!.Legs);
        Assert.Equal(new SaveTripRequest.Place(stop.PlaceId, type), Assert.Single(replacement.Stops!));
        Assert.Equal(roundTrip, replacement.IsRoundTrip);
        Assert.Equal(stop, Assert.Single(page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>().Instance.Stops));

        var reopened = Render<TripDetails>(parameters => parameters.Add(details => details.TripId, TripId));
        reopened.WaitForAssertion(() => Assert.Contains("Only place", reopened.Markup));
        Assert.Empty(reopened.FindAll(".trip-legs li"));
    }


    [Theory]
    [InlineData("attraction", "Attraction 1")]
    [InlineData("transport_stop", "Stop 1")]
    public async Task SingleNewStopResolvesItsCatalogueEntryWithoutCreatingTransportOrALeg(string type, string name)
    {
        var place = new TripPlaceDto(Guid.NewGuid(), type, name, "", 45.51m, -73.58m);
        SaveTripRequest? created = null;
        var catalogueCreates = 0;
        Configure(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == (type == "attraction" ? "/api/v1/attractions" : "/api/v1/route-points"))
            {
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) };
                Assert.Equal(HttpMethod.Post, request.Method);
                catalogueCreates++;
                if (type == "transport_stop")
                {
                    var fields = await request.Content!.ReadFromJsonAsync<System.Text.Json.JsonElement>(token);
                    Assert.Equal("private", fields.GetProperty("visibility").GetString());
                    Assert.Equal(type, fields.GetProperty("type").GetString());
                    Assert.Equal(name, fields.GetProperty("name").GetString());
                    return new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = JsonContent.Create(new RoutePointDto(place.PlaceId, type, name, place.Address, place.Lat, place.Lon, null))
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new AttractionDto(place.PlaceId, place.Name, null, null,
                        place.Address, place.Lat, place.Lon, 0))
                };
            }
            Assert.Equal("/api/v1/trips", request.RequestUri.AbsolutePath);
            Assert.Equal(HttpMethod.Post, request.Method);
            created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(Saved with { Name = created!.Name, Legs = [], Stops = [place] });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        page.Find(".map-trip-name").Change("One place");
        SelectTripDates(page, new(2026, 11, 14), new(2026, 11, 14));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync((place.Type, place.Lat, place.Lon)));
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Equal(1, catalogueCreates));
        page.WaitForAssertion(() => Assert.NotNull(created));
        Assert.Empty(created!.Legs);
        Assert.Equal(new SaveTripRequest.Place(place.PlaceId, place.Type), Assert.Single(created.Stops!));
        Assert.Equal(place, Assert.Single(canvas.Instance.Stops));
    }


    [Fact]
    public async Task MapToolsTogglePreservesSelectedStopAndUnconfirmedEditBuffer()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.Contains(page.FindAll("button"), button => button.TextContent.Trim() == "Basics");
        Assert.Single(page.FindAll(".trip-chain .trip-basics"));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("Pending name");
        var detailsPanel = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last();
        var body = detailsPanel.Find(".movable-panel-body");
        body.SetAttribute("inert", "");
        Assert.Null(page.Find("[data-trip-save]").Closest(".movable-panel-body"));
        page.Find(".map-trip-name").Change("Changed while tools are collapsed");
        body.RemoveAttribute("inert");
        Assert.Equal("Pending name", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(1, canvas.Instance.SelectedIndex);
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        Assert.Same(detailsPanel.Instance, page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last().Instance);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostPrivateStopResponseRetriesLookupWithoutCreatingAnotherRecord(bool committed)
    {
        var trip = Saved with { Legs = [Saved.Legs[0] with { To = Museum with { Type = "transport_stop" } }] };
        var previous = new RoutePointDto(Guid.NewGuid(), "transport_stop", Museum.Name, "", 45.52m, -73.59m, null);
        var created = previous with { RoutePointId = Guid.NewGuid(), Name = " MUSEUM " };
        var nearMatch = created with { RoutePointId = Guid.NewGuid(), Lon = -73.58m };
        var catalogueWrites = 0;
        var tripWrites = 0;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/route-points")
            {
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Contains("name=Museum", request.RequestUri.Query);
                    RoutePointDto[] results = committed && catalogueWrites > 0 ? [previous, nearMatch, created] : [previous];
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(results) };
                }

                Assert.Equal(HttpMethod.Post, request.Method);
                catalogueWrites++;
                throw new HttpRequestException("Response lost after the create request.");
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get)
                return Json(trip);

            Assert.Equal(HttpMethod.Put, request.Method);
            tripWrites++;
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            Assert.Equal(created.RoutePointId, Assert.Single(body.Legs).To.PlaceId);
            return Json(trip);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, previous.Lat + 0.000003m, previous.Lon - 0.000004m)));

        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
        Click(page, "Save trip");

        if (committed)
        {
            page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
            Assert.Equal(1, tripWrites);
        }
        else
        {
            page.WaitForAssertion(() => Assert.Contains("No saved record matches Museum", page.Find("[role=alert]").TextContent));
            Assert.DoesNotContain(page.FindAll(".trip-review button"), button => button.TextContent == "Save trip");
            Assert.Equal(0, tripWrites);
        }

        Assert.Equal(1, catalogueWrites);
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapJoinsNonAdjacentStopsAndPreservesTheFirstJourneyWhenClosingALoop(bool addReturnFirst)
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not call the catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.DropMarker("accommodation", 350, 200));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("attraction", 430, 220));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("transport_stop", 390, 290));
        Click(page, "Transport");

        if (addReturnFirst)
            Connect("Walk", "Attraction 2", "Accommodation 1");
        Connect("Train", "Accommodation 1", "Stop 3");
        Connect("Bus", "Stop 3", "Attraction 2");
        if (!addReturnFirst)
            Connect("Walk", "Attraction 2", "Accommodation 1");

        Assert.Equal(addReturnFirst
                ? new[] { "Attraction 2", "Accommodation 1", "Stop 3", "Attraction 2" }
                : ["Accommodation 1", "Stop 3", "Attraction 2", "Accommodation 1"],
            page.FindAll(".trip-stop strong").Select(name => name.TextContent));
        Assert.Equal(addReturnFirst ? new[] { "Walk", "Train", "Bus" } : ["Train", "Bus", "Walk"],
            page.FindAll(".map-leg-label span").Select(label => label.TextContent));
        Assert.Equal(3, page.FindAll(".route-line").Count);

        Click(page, "Review trip");
        Assert.Contains("Round trip", page.Find(".trip-review small").TextContent);

        void Connect(string mode, string from, string to)
        {
            Click(page, mode);
            page.Find($".trip-marker[aria-label$=': {from}']").Click();
            page.Find($".trip-marker[aria-label$=': {to}']").Click();
        }
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapPlannerSavesSavedPlacesAndBrowserCheckpointAfterModeCatalogueRetry(bool catalogueInitiallyEmpty)
    {
        SaveTripRequest? created = null;
        var modeWrites = 0;
        var modeReads = 0;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            switch (path)
            {
                case "/api/v1/accommodations":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AccommodationId = Hotel.PlaceId, Hotel.Name, Hotel.Address, Hotel.Lat, Hotel.Lon } }) };
                case "/api/v1/attractions":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AttractionId = Museum.PlaceId, Museum.Name, Museum.Address, Museum.Lat, Museum.Lon, EstimatedVisitMinutes = 30 } }) };
                case "/api/v1/transport-modes":
                    if (request.Method == HttpMethod.Get)
                    {
                        Assert.Equal("?name=&code=", request.RequestUri.Query);
                        var modes = catalogueInitiallyEmpty && modeReads++ == 0 ? Array.Empty<TripDto.Mode>() : [Train];
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(modes) };
                    }

                    modeWrites++;
                    throw new InvalidOperationException("Trip planning must never create transport modes.");
                case "/api/v1/trips":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
                    return Json(Saved);
                default:
                    throw new InvalidOperationException($"Unexpected request path {path}.");
            }
        });
        ConfigureBrowserDraftStorage();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/map");
        var page = Render<NewTrip>();

        page.Find(".map-trip-name, .trip-basics input").Change("Weekend");
        SelectTripDates(page, new(2026, 11, 14), new(2026, 11, 14));
        Click(page, "Markers");
        Click(page, "Accommodation");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Hotel", page.Find(".trip-search-results").TextContent));
        Click(page, "Hotel");
        Click(page, "Attraction");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Museum", page.Find(".trip-search-results").TextContent));
        Click(page, "Museum");
        Click(page, "Transport");
        Click(page, "Train");
        page.FindAll(".trip-stop")[0].Click();
        page.FindAll(".trip-stop")[1].Click();
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.Find("input[aria-label='Duration hours']").Change("1");
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-14T10:00:00");
        Assert.Contains("Train · 1 h 0 min", page.Find(".map-leg-label").TextContent);
        Assert.StartsWith("2026-11-14T11:00", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));

        page.Find("input[aria-label='Duration minutes']").Change("60");
        Assert.Contains("Enter whole minutes from 0 to 59.", page.Find("[role=alert]").TextContent);
        page.Find("input[aria-label='Duration minutes']").Change("20");
        Assert.StartsWith("2026-11-14T11:20", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));

        page.FindAll(".leg-panel input[type=datetime-local]")[1].Change("2026-11-14T11:15");
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("15", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.Contains("Train · 1 h 15 min", page.Find(".map-leg-label").TextContent);
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-14T10:15");
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("0", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.StartsWith("2026-11-14T11:15", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));
        Click(page, "Review trip");
        Assert.True(page.FindAll("[role=alert]").Count == 0, string.Join(" ", page.FindAll("[role=alert]").Select(alert => alert.TextContent)));
        Click(page, "Save map draft");
        Click(page, "Save trip");
        if (catalogueInitiallyEmpty)
        {
            page.WaitForAssertion(() => Assert.Contains("Transport modes unavailable", page.Find("[role=alert]").TextContent));
            Click(page, "Save trip");
        }

        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[role=status]"), item => item.TextContent.Contains("Saved Weekend")));
        Assert.Collection(JSInterop.Invocations["localStorage.setItem"],
            item => Assert.EndsWith("-new", Assert.IsType<string>(item.Arguments[0])),
            item => Assert.EndsWith($"-{TripId:D}", Assert.IsType<string>(item.Arguments[0])));
        Assert.Equal(0, modeWrites);
        Assert.NotNull(created);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 0, 0, 0, Start.Offset), created.Start);
        var leg = Assert.Single(created.Legs);
        Assert.Equal(Hotel.PlaceId, leg.From.PlaceId);
        Assert.Equal(Museum.PlaceId, leg.To.PlaceId);
        Assert.Equal(Train.TransportModeId, leg.TransportModeId);
        Assert.Equal(Start.AddMinutes(75), leg.Departure);
        Assert.Equal(Start.AddMinutes(135), leg.Arrival);
        Assert.EndsWith($"/map/{TripId:D}", navigation.Uri);
    }


    [Fact]
    public void UnavailableMapTripRequestsAttentionForItsLoadFailure()
    {
        Configure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("unavailable", page.Find("[role=alert]").TextContent);
            Assert.True(page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last().Instance.RevealVersion > 0);
        });
    }

    [Fact]
    public void StandardTripStepsKeepTheDraftAndUseANonMapReview()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/trip/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var editor = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last().Instance;
        page.Find(".trip-basics input").Change("Weekend with friends");

        Click(page, "Continue to itinerary");
        Assert.Contains("Step 2 of 3", page.Find(".trip-progress").TextContent);
        Click(page, "Back to basics");
        Assert.Equal("Weekend with friends", page.Find(".trip-basics input").GetAttribute("value"));
        Click(page, "Continue to itinerary");
        Click(page, "Review trip");

        Assert.Contains("Step 3 of 3", page.Find(".trip-progress").TextContent);
        Assert.Empty(page.FindAll(".trip-map-canvas, .movable-panel-handle"));
        Assert.Contains("Weekend with friends", page.Find("[aria-label='Trip review details']").TextContent);
        Assert.Same(editor, page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Last().Instance);
        Click(page, "Back to stops");
        Assert.Contains("Step 2 of 3", page.Find(".trip-progress").TextContent);
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
    }

    [Fact]
    public void MapWorkspaceKeepsAccessibleNewThenSaveWithoutAnOpenSavedToolbar()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        Assert.Equal(new[] { "New trip", "Save trip" }, page.FindAll(".trip-workspace-actions button").Select(button => button.GetAttribute("aria-label")));
        Assert.Single(page.FindAll("[data-trip-save]"));
        Assert.Contains("Control+S", page.Find("[data-trip-save]").GetAttribute("aria-keyshortcuts"));
        Assert.Empty(page.FindAll(".trip-workspace-actions [aria-controls=saved-trip-browser]"));
        Assert.Equal(3, page.FindAll(".movable-panel-handle").Count);
    }

    [Theory]
    [InlineData("map")]
    [InlineData("trip")]
    public async Task WorkspaceSaveIsAvailableWithoutReviewAndBlocksDuplicateRequests(string route)
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        Configure((request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Json(Saved));
            Assert.Equal(HttpMethod.Put, request.Method);
            writes++;
            return response.Task;
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/{route}/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-trip-save]:enabled")));
        Assert.Empty(page.FindAll(".trip-review"));
        Assert.Equal("Control+S Meta+S", page.Find("[data-trip-save]").GetAttribute("aria-keyshortcuts"));

        var saving = page.Find("[data-trip-save]").ClickAsync(new MouseEventArgs());
        page.WaitForAssertion(() => Assert.Equal(1, writes));
        Assert.True(page.Find("[data-trip-save]").HasAttribute("disabled"));
        Assert.True(page.Find("[data-review-save]").HasAttribute("disabled"));
        await page.Find("[data-review-save]").ClickAsync(new MouseEventArgs());
        Assert.Equal(1, writes);
        response.SetResult(Json(Saved));
        await saving;
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find(".trip-message.success").TextContent));
    }

    [Theory]
    [InlineData("America/Toronto", "2026-11-14", "2026-11-14", "2026-11-14T00:00:00-05:00", "2026-11-14T23:59:59.9999999-05:00")]
    [InlineData("America/Toronto", "2026-11-14", "2026-11-15", "2026-11-14T00:00:00-05:00", "2026-11-15T23:59:59.9999999-05:00")]
    [InlineData("America/Toronto", "2026-03-08", "2026-03-08", "2026-03-08T00:00:00-05:00", "2026-03-08T23:59:59.9999999-04:00")]
    [InlineData("America/Sao_Paulo", "2018-11-04", "2018-11-04", "2018-11-04T01:00:00-02:00", "2018-11-04T23:59:59.9999999-02:00")]
    [InlineData("America/Havana", "2025-11-02", "2025-11-02", "2025-11-02T00:00:00-04:00", "2025-11-02T23:59:59.9999999-05:00")]
    public void DateRangeSavesWholeLocalDaysIncludingSameDayAndDaylightSaving(string zone, string first, string last, string expectedFirst, string expectedLast)
    {
        var start = DateTimeOffset.Parse(expectedFirst);
        var end = DateTimeOffset.Parse(expectedLast);
        var trip = Saved with { TimeZone = zone, Start = start.AddHours(1), End = end.AddHours(-1),
            Legs = [Saved.Legs[0] with { Departure = start.AddHours(6), Arrival = start.AddHours(7) }] };
        SaveTripRequest? updated = null;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(trip);
            updated = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(trip with { Start = updated!.Start, End = updated.End });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(component => component.TripId, TripId));
        page.Find(".range-trigger").Click();
        page.Find($"[data-date='{first}']").Click();
        page.Find($"[data-date='{last}']").Click();
        page.Find("[data-trip-save]").Click();
        page.WaitForAssertion(() => Assert.NotNull(updated));
        Assert.Equal(start, updated!.Start);
        Assert.Equal(end, updated.End);
        Assert.Equal(start.Offset, updated.Start.Offset);
        Assert.Equal(end.Offset, updated.End.Offset);
    }

    [Fact]
    public void DateRangeIsOneUndoableEditAndUndoRestoresPreciseSavedBounds()
    {
        var precise = Saved with { Start = Saved.Start.AddTicks(1234567), End = Saved.End.AddTicks(7654321) };
        SaveTripRequest? updated = null;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(precise);
            updated = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(precise);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(component => component.TripId, TripId));
        page.Find(".range-trigger").Click();
        page.Find("[data-date='2026-11-15']").Click();
        Assert.True(page.Find("[data-trip-undo]").HasAttribute("disabled"));
        page.Find("[data-date='2026-11-16']").Click();
        page.Find("[data-trip-undo]").Click();
        Assert.True(page.Find("[data-trip-undo]").HasAttribute("disabled"));
        page.Find("[data-trip-save]").Click();
        page.WaitForAssertion(() => Assert.NotNull(updated));
        Assert.Equal(precise.Start, updated!.Start);
        Assert.Equal(precise.End, updated.End);
    }

    [Theory]
    [InlineData(" Museum ", "")]
    [InlineData("Museum", "   ")]
    public async Task AppliedWhitespaceOnlyMarkerEditsKeepCatalogueReferenceAndSave(string name, string address)
    {
        SaveTripRequest? savedRequest = null;
        Configure(async (request, token) =>
        {
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get) return Json(Saved);
            Assert.Equal(HttpMethod.Put, request.Method);
            savedRequest = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(Saved);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.FindAll(".trip-place input[type=text]")[0].Change(name);
        page.Find(".address-search input").Input(address);

        page.Find(".trip-place").Submit();
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        page.Find("[data-trip-save]").Click();

        page.WaitForAssertion(() => Assert.NotNull(savedRequest));
        Assert.Equal(Museum.PlaceId, savedRequest!.Legs.Single().To.PlaceId);
        Assert.Contains("Saved Weekend", page.Find(".trip-message.success").TextContent);
    }

    [Theory]
    [InlineData(" Updated museum ", "", false)]
    [InlineData("Museum", " New address ", false)]
    [InlineData(" Updated museum ", "", true)]
    public async Task OneSavePersistsMarkerChangesAndTrip(string name, string address, bool applyFirst)
    {
        var confirmed = Museum with { PlaceId = Guid.Parse("00000000-0000-0000-0000-000000009004"), Name = name.Trim(), Address = address.Trim() };
        SaveTripRequest? savedRequest = null;
        var catalogueWrites = 0;
        Configure(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/attractions")
            {
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) };
                Assert.Equal(HttpMethod.Post, request.Method);
                var body = await request.Content!.ReadFromJsonAsync<JsonObject>(token);
                Assert.Equal(confirmed.Name, body!["name"]!.GetValue<string>());
                Assert.Equal(confirmed.Address, body["address"]!.GetValue<string>());
                catalogueWrites++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new AttractionDto(confirmed.PlaceId, confirmed.Name, null, null,
                        confirmed.Address, confirmed.Lat, confirmed.Lon, 0))
                };
            }
            if (request.Method == HttpMethod.Get) return Json(Saved);
            Assert.Equal(HttpMethod.Put, request.Method);
            savedRequest = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(Saved with { Legs = [Saved.Legs[0] with { To = confirmed }] });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.FindAll(".trip-place input[type=text]")[0].Change(name);
        page.Find(".address-search input").Input(address);

        if (applyFirst)
        {
            page.Find(".trip-place").Submit();
            Assert.Equal(Guid.Empty, canvas.Instance.Stops[1].PlaceId);
        }
        page.Find("[data-trip-save]").Click();

        Assert.Empty(page.FindAll("[role=alert]"));
        page.WaitForAssertion(() => Assert.NotNull(savedRequest));
        Assert.Equal(1, catalogueWrites);
        Assert.Equal(confirmed.PlaceId, savedRequest!.Legs.Single().To.PlaceId);
        Assert.Contains("Saved Weekend", page.Find(".trip-message.success").TextContent);
    }

    [Theory]
    [InlineData("map", true)]
    [InlineData("trip", false)]
    public void MovementControlsBelongOnlyToMapEvenDuringReview(string route, bool movable)
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/{route}/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var editor = page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Single(panel => panel.Instance.Label == "Plan stops and routes").Instance;
        Assert.All(page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>(), panel => Assert.Equal(movable, panel.Instance.Enabled));
        Assert.DoesNotContain("Reset panel layout", page.Markup);
        Assert.DoesNotContain("Standard editor", page.Markup);
        page.Find(".map-trip-name, .trip-basics input").Change("Unsaved panel edit");
        Click(page, "Review trip");
        Assert.Same(editor, page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>().Single(panel => panel.Instance.Label == "Plan stops and routes").Instance);
        if (movable) Assert.Equal("Unsaved panel edit", page.Find(".map-trip-name").GetAttribute("value"));
        else Assert.Contains("Unsaved panel edit", page.Find("[aria-label='Trip review details']").TextContent);
        Assert.All(page.FindComponents<TravelTracker.Frontend.Components.Trips.MovablePanel>(), panel => Assert.Equal(movable, panel.Instance.Enabled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TripNavigationConfirmsBeforeDiscardingUnconfirmedMarkerDetails(bool discard)
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(discard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/map/{TripId:D}");
        var routes = Render<Routes>();
        var page = routes.FindComponent<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("Unconfirmed museum");

        await routes.InvokeAsync(() => navigation.NavigateTo($"/trip/{TripId:D}"));

        routes.WaitForAssertion(() =>
        {
            Assert.Single(JSInterop.Invocations["confirmTripChanges"]);
            Assert.EndsWith($"/{(discard ? "trip" : "map")}/{TripId:D}", navigation.Uri);
            if (!discard)
                Assert.Equal("Unconfirmed museum", page.Find(".trip-place input[type=text]").GetAttribute("value"));
            else
                Assert.Empty(page.FindAll(".trip-map-canvas"));
        });
        Assert.Equal(Museum.Name, page.FindAll(".trip-stop strong")[1].TextContent);
    }

    [Fact]
    public async Task TravellerReplacesDraftTransportStopAndSavesWithoutWritingToCatalogue()
    {
        var original = Museum with { Type = "transport_stop", Name = "Original station" };
        var replacement = original with { PlaceId = Guid.Parse("00000000-0000-0000-0000-000000009014"), Name = "Saved station" };
        var outbound = Saved.Legs[0] with { To = original };
        var onward = new TripDto.Leg(2, original, Museum, Train, Start.AddHours(2), Start.AddHours(3));
        var trip = Saved with { Legs = [outbound, onward] };
        var updated = trip with { Legs = [outbound with { To = replacement }, onward with { From = replacement }] };
        SaveTripRequest? saved = null;
        Configure(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/route-points")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("type=transport_stop", request.RequestUri.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new RoutePointDto(replacement.PlaceId, replacement.Type,
                        replacement.Name, replacement.Address, replacement.Lat, replacement.Lon, null) })
                };
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(trip);
            Assert.Equal(HttpMethod.Put, request.Method);
            saved = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(updated, "\"2\"");
        }, scopes: "catalogue.read catalogue.write users.read users.write settings.read settings.write trips.read trips.write");
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.52m, -73.59m)));

        Click(page, "Review trip");
        Assert.Contains("saves new locations privately", page.Markup);
        Assert.Single(page.FindAll("[data-review-save]"));
        Click(page, "Back to stops");
        page.FindAll(".trip-stop")[1].Click();
        Assert.Single(page.FindAll(".address-search"));
        page.Find(".trip-search input").Change("Saved station");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".trip-search-results button")));
        page.Find(".trip-search-results button").Click();
        Assert.Equal(3, page.FindAll(".trip-stop").Count);

        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
        Assert.Collection(saved!.Legs, first =>
        {
            Assert.Equal(replacement.PlaceId, first.To.PlaceId);
            Assert.Equal(Train.TransportModeId, first.TransportModeId);
            Assert.Equal(outbound.Departure, first.Departure);
        }, second =>
        {
            Assert.Equal(replacement.PlaceId, second.From.PlaceId);
            Assert.Equal(Museum.PlaceId, second.To.PlaceId);
        });
    }

    [Fact]
    public async Task PastedSavedTripLinkFitsTheNewTripInsteadOfKeepingThePreviousCamera()
    {
        var otherId = Guid.NewGuid();
        var parisHotel = Hotel with { PlaceId = Guid.NewGuid(), Name = "Paris hotel", Lat = 48.86m, Lon = 2.35m };
        var parisMuseum = Museum with { PlaceId = Guid.NewGuid(), Name = "Paris museum", Lat = 48.87m, Lon = 2.36m };
        var other = Saved with { TripId = otherId, Legs = [Saved.Legs[0] with { From = parisHotel, To = parisMuseum }] };
        Configure((request, _) => Task.FromResult(Json(request.RequestUri!.AbsolutePath.EndsWith(otherId.ToString()) ? other : Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.PanByDrag(4000, 0));
        Assert.Empty(page.FindAll(".trip-marker"));

        page.Find(".trip-open input").Change($"https://localhost/map/{otherId:D}");
        page.Find(".trip-open form").Submit();

        page.WaitForAssertion(() =>
        {
            Assert.Equal(otherId, canvas.Instance.TripId);
            Assert.Equal(2, page.FindAll(".trip-marker").Count);
            Assert.Equal(parisHotel, canvas.Instance.Stops[0]);
        });
    }

    [Fact]
    public async Task SameTypePaletteStartsANewMarkerInsteadOfEditingTheSelectedSavedStop()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        Assert.False(canvas.Instance.CanPick);

        Click(page, "Attraction");

        Assert.True(canvas.Instance.CanPick);
        Assert.Equal("", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        page.Find("button[aria-label='Pick map center']").Click();
        Assert.Equal(3, canvas.Instance.Stops.Count);
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        Assert.Equal(Guid.Empty, canvas.Instance.Stops[2].PlaceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateNewFromTripWorkspaceHandlesUnsavedChangesBeforeReplacingDraft(bool discard)
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(discard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/trip/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find(".trip-basics input").Change("Unsaved title");
        Assert.Single(page.FindAll("button"), button => button.TextContent == "Open saved");

        Click(page, "Create new");

        Assert.Single(JSInterop.Invocations["confirmTripChanges"]);
        Assert.Equal(discard ? "" : "Unsaved title", page.Find(".trip-basics input").GetAttribute("value"));
        Assert.Equal(discard ? 0 : 2, page.FindAll(".trip-stop").Count);
        Assert.EndsWith(discard ? "/trip" : $"/trip/{TripId:D}", navigation.Uri);
    }

    [Fact]
    public void OpenSavedBrowsesHistoryInsideTheWorkspaceWithoutDiscardingTheDraft()
    {
        Configure((request, _) =>
        {
            Assert.Equal("/api/v1/trips", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new TripSummaryDto(TripId, Saved.Name, Saved.Start, Saved.End) })
            });
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/trip");
        var page = Render<NewTrip>();
        page.Find(".trip-basics input").Change("Keep my draft");
        Click(page, "Open saved");
        page.WaitForAssertion(() => Assert.Single(page.FindAll($"#saved-trip-browser a[href='map/{TripId:D}']")));
        Assert.Equal("Keep my draft", page.Find(".trip-basics input").GetAttribute("value"));
        Assert.EndsWith("/trip", navigation.Uri);
        Assert.Empty(JSInterop.Invocations["confirmTripChanges"]);
        Click(page, "Open saved");
        Assert.Empty(page.FindAll("#saved-trip-browser"));
        Assert.Equal("Keep my draft", page.Find(".trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public async Task MapPlanningStepsPreserveSelectedStopAndUnconfirmedEditBuffer()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.Equal(new[] { "Basics", "Stops", "Review trip" }, page.FindAll(".trip-steps button").Select(button => button.TextContent));
        Assert.Single(page.FindAll(".trip-chain .trip-basics"));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("Pending name");
        Click(page, "Basics");
        Assert.Equal("true", page.FindAll(".trip-steps button")[0].GetAttribute("aria-pressed"));
        Click(page, "Stops");
        Assert.Equal("true", page.FindAll(".trip-steps button")[1].GetAttribute("aria-pressed"));
        Assert.Equal("Pending name", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(1, canvas.Instance.SelectedIndex);
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
    }

    [Fact]
    public async Task MarkerEditingKeepsCancelAndInvalidBuffersAndSavesTheConfirmedNewPlace()
    {
        var renamedId = Guid.NewGuid();
        var name = "Museum <script>alert(1)</script>";
        SaveTripRequest? savedRequest = null;
        var failSave = true;
        Configure(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/attractions")
            {
                object result = request.Method == HttpMethod.Get ? Array.Empty<AttractionDto>()
                    : new AttractionDto(renamedId, name, null, null, "New address", Museum.Lat, Museum.Lon, 0);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
            }
            if (request.Method == HttpMethod.Get) return Json(Saved);
            savedRequest = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            return failSave ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(Saved with { Legs = [Saved.Legs[0] with { To = Museum with { PlaceId = renamedId, Name = name, Address = "New address" } }] });
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        Assert.Contains("Edit stop details", page.Markup);
        Assert.False(canvas.Instance.CanPick);
        page.Find(".trip-place input[type=text]").Change("Discard me");
        Click(page, "Cancel");
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("");
        page.Find(".trip-place").Submit();
        Assert.Contains("Enter a place name", page.Find("[role=alert]").TextContent);
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        page.Find(".trip-place input[type=text]").Change(name);
        page.Find(".address-search input").Input("New address");
        page.Find(".trip-place").Submit();
        Assert.Equal(Guid.Empty, canvas.Instance.Stops[1].PlaceId);
        Assert.Equal(name, page.FindAll(".trip-stop strong")[1].TextContent);
        Assert.Empty(page.FindAll("script"));
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.NotNull(savedRequest));
        Assert.Equal(renamedId, savedRequest!.Legs[0].To.PlaceId);
        Assert.Contains(name, page.FindAll(".trip-stop strong")[1].TextContent);
        failSave = false;
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Markup));
        Assert.Equal(renamedId, canvas.Instance.Stops[1].PlaceId);
        Assert.Equal(name, canvas.Instance.Stops[1].Name);
    }

    [Fact]
    public void MinuteInputsPreserveUntouchedSavedSecondsAndNormalizeEditedTimes()
    {
        var instant = Start.AddSeconds(37).AddTicks(1234);
        var trip = Saved with
        {
            Start = instant, End = instant.AddHours(4),
            Legs = [new(1, Hotel, Museum, Train, instant.AddHours(1), instant.AddHours(2))]
        };
        var writes = new List<SaveTripRequest>();
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Json(trip);
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            writes.Add(body);
            trip = trip with { Name = body.Name, Start = body.Start, End = body.End,
                Legs = [trip.Legs[0] with { Departure = body.Legs[0].Departure, Arrival = body.Legs[0].Arrival }] };
            return Json(trip);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.Empty(page.FindAll(".trip-basics input[type=datetime-local]"));
        Assert.Equal(new DateOnly(2026, 11, 14), page.FindComponent<TravelTracker.Frontend.Components.Trips.TripDateRangePicker>().Instance.Start);
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        var unchangedTimes = page.FindAll(".leg-fields input[type=datetime-local]");
        unchangedTimes[0].Change(unchangedTimes[0].GetAttribute("value"));
        var unchangedArrival = page.FindAll(".leg-fields input[type=datetime-local]")[1];
        unchangedArrival.Change(unchangedArrival.GetAttribute("value"));
        page.Find(".map-trip-name, .trip-basics input").Change("Renamed trip");
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Single(writes));
        Assert.Equal(instant, writes[0].Start);
        Assert.Equal(instant.AddHours(1), writes[0].Legs[0].Departure);

        Click(page, "Back to editor");
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        var times = page.FindAll(".leg-fields input[type=datetime-local]");
        Assert.All(times, input => Assert.Equal("60", input.GetAttribute("step")));
        times[0].Change("2026-11-14T10:05:59.123");
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Equal(2, writes.Count));
        Assert.Equal(Start.AddHours(1).AddMinutes(5), writes[1].Legs[0].Departure);
        Assert.Equal(instant.AddHours(2), writes[1].Legs[0].Arrival);
    }

    [Theory]
    [InlineData(false, "sessionStorage", "localStorage")]
    [InlineData(true, "localStorage", "sessionStorage")]
    public async Task MapNameAndIncompleteDraftCanBeSavedAndRestoredWithoutTripsApi(
        bool signedIn, string storage, string unusedStorage)
    {
        Configure((_, _) => throw new InvalidOperationException("A browser draft must not call the Trips API."));
        ConfigureBrowserDraftStorage(signedIn);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        page.Find(".map-trip-name").Change("Unfinished weekend");
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("attraction", 45.51m, -73.58m)));
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("transport_stop", 45.52m, -73.59m)));

        Click(page, "Save map draft");
        var saved = Assert.Single(JSInterop.Invocations[$"{storage}.setItem"]);
        Assert.Empty(JSInterop.Invocations[$"{unusedStorage}.setItem"]);
        var key = Assert.IsType<string>(saved.Arguments[0]);
        var value = Assert.IsType<string>(saved.Arguments[1]);
        Assert.Contains("Map draft saved in this browser.", page.Markup);
        Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        Assert.False(page.Find("[data-trip-undo]").HasAttribute("disabled"));

        JSInterop.Setup<string>($"{storage}.getItem", call => Equals(call.Arguments[0], key)).SetResult(value);
        var reopened = Render<NewTrip>();
        Click(reopened, "Restore map draft");
        Assert.DoesNotContain(JSInterop.Invocations[$"{unusedStorage}.getItem"], call => call.Arguments[0] is string key && key.StartsWith("traveltracker-map-draft", StringComparison.Ordinal));
        Assert.Equal("Unfinished weekend", reopened.Find(".map-trip-name").GetAttribute("value"));
        Assert.Equal(new[] { 45.51m, 45.52m }, reopened
            .FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>().Instance.Stops.Select(stop => stop.Lat));
        Assert.True(reopened.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedBrowserConnectionsDoNotReplaceTheCurrentMap(bool duplicate)
    {
        Configure((_, _) => throw new InvalidOperationException("A browser draft must not call the Trips API."));
        ConfigureBrowserDraftStorage();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("attraction", 45.51m, -73.58m)));
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("transport_stop", 45.52m, -73.59m)));
        Click(page, "Save map draft");
        var original = Assert.Single(JSInterop.Invocations["localStorage.setItem"]);
        var key = Assert.IsType<string>(original.Arguments[0]);
        var readingOriginal = true;
        JSInterop.Setup<string>("localStorage.getItem", call => readingOriginal && Equals(call.Arguments[0], key))
            .SetResult(Assert.IsType<string>(original.Arguments[1]));
        var storage = Services.GetRequiredService<ProtectedLocalStorage>();
        var snapshot = (await storage.GetAsync<JsonObject>(key)).Value!;
        var legs = snapshot["map"]!["legs"]!.AsArray();
        legs.Clear();
        legs.Add(new JsonObject { ["from"] = 0, ["to"] = duplicate ? 1 : 99 });
        if (duplicate)
            legs.Add(new JsonObject { ["from"] = 0, ["to"] = 1 });
        await storage.SetAsync(key, snapshot);
        readingOriginal = false;
        var malformed = JSInterop.Invocations["localStorage.setItem"].Last();
        JSInterop.Setup<string>("localStorage.getItem", call => Equals(call.Arguments[0], key))
            .SetResult(Assert.IsType<string>(malformed.Arguments[1]));
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(true);
        page.Find(".map-trip-name").Change("Keep these edits");

        Click(page, "Restore map draft");

        Assert.Contains("The browser draft could not be restored.", page.Markup);
        Assert.Equal("Keep these edits", page.Find(".map-trip-name").GetAttribute("value"));
        Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    [Fact]
    public void BrowserStorageUnavailableDoesNotFaultThePlanner()
    {
        Configure((_, _) => throw new InvalidOperationException("A browser draft must not call the Trips API."));
        ConfigureBrowserDraftStorage();
        JSInterop.SetupVoid("localStorage.setItem", _ => true)
            .SetException(new InvalidOperationException("JS interop is unavailable."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        page.Find(".map-trip-name").Change("Keep working");

        Click(page, "Save map draft");

        Assert.Contains("The browser draft could not be saved.", page.Markup);
        Assert.True(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        Assert.False(page.Find("[data-trip-undo]").HasAttribute("disabled"));
    }

    [Fact]
    public void FailedUpdateKeepsDraftForRetryAndFreshTripDoesNotReuseSavedId()
    {
        var writes = new List<SaveTripRequest>();
        Configure(async (request, token) =>
        {
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(writes.Count < 2 ? "\"1\"" : "\"2\"", Assert.Single(request.Headers.IfMatch).Tag);
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            writes.Add(body);
            return writes.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(Saved with { Name = body.Name }, "\"2\"");
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/map/{TripId:D}");
        var routes = Render<Routes>();
        var page = routes.FindComponent<NewTrip>();

        NavigateToTrip(page);
        Assert.EndsWith($"/trip/{TripId:D}", navigation.Uri);
        page.Find(".map-trip-name, .trip-basics input").Change("Updated weekend");
        Click(page, "Review trip");
        Assert.Contains("Updated weekend", page.Find("[aria-label='Trip review details']").TextContent);
        Assert.Equal(new[] { "01HotelAccommodation", "02MuseumAttraction" },
            page.FindAll(".trip-stop").Select(stop => string.Concat(stop.TextContent.Where(character => !char.IsWhiteSpace(character)))));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("entries are retained", page.Find("[role=alert]").TextContent));
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Updated weekend", page.Find("[role=status]").TextContent));
        Assert.EndsWith($"/trip/{TripId:D}", navigation.Uri);
        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Legs.Single(), writes[1].Legs.Single());
        Assert.Equal(Train.TransportModeId, writes[1].Legs.Single().TransportModeId);

        Click(page, "Back to editor");
        page.Find(".map-trip-name, .trip-basics input").Change("Edited again");
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Saved Edited again", page.Find("[role=status]").TextContent));
        Assert.Equal(3, writes.Count);

        navigation.NavigateTo("/new-trip");
        routes.WaitForAssertion(() =>
        {
            Assert.Equal("Plan your trip", routes.Find("h1").TextContent);
            Assert.Empty(routes.FindAll(".trip-stop"));
            Assert.Equal("", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
        });
    }

    [Fact]
    public void StaleSaveKeepsTheDraftAndItsOriginalRevision()
    {
        var attempts = 0;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(Saved, "\"original\"");

            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("\"original\"", Assert.Single(request.Headers.IfMatch).Tag);
            var body = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            Assert.Equal("My edit", body!.Name);
            attempts++;
            return new HttpResponseMessage(HttpStatusCode.PreconditionFailed);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/new-trip/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        page.Find(".map-trip-name, .trip-basics input").Change("My edit");
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Reopen the trip", page.Find("[role=alert]").TextContent));
        Assert.Equal(1, attempts);
        Assert.Contains("My edit", page.Find("[aria-label='Trip review details']").TextContent);
        Click(page, "Back to stops");
        Assert.Equal("My edit", page.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public async Task FailedRouteLoadRestoresTheCurrentTripsLink()
    {
        var unavailable = Guid.Parse("d5155a18-c22d-46f6-911a-3558b4e3985c");
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            return path == $"/api/v1/trips/{TripId:D}" ? Task.FromResult(Json(Saved))
                : path == $"/api/v1/trips/{unavailable:D}" ? response.Task
                : throw new InvalidOperationException($"Unexpected request path {path}.");
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/new-trip/{TripId:D}");
        var routes = Render<Routes>();

        await routes.InvokeAsync(() => navigation.NavigateTo($"/new-trip/{unavailable:D}"));
        Assert.Contains("Opening saved trip", routes.Markup);
        Assert.DoesNotContain(Saved.Name, routes.Markup);
        Assert.Empty(routes.FindAll(".trip-basic-fields, .trip-marker"));
        response.SetResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        routes.WaitForAssertion(() =>
        {
            Assert.EndsWith($"/new-trip/{TripId:D}", navigation.Uri);
            Assert.Contains("That saved trip is unavailable.", routes.Find("[role=alert]").TextContent);
            Assert.Equal(Saved.Name, routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedTripSaveAndRetryDoNotWriteToTheCatalogue(bool restoreExistingPlace)
    {
        var catalogueWrites = 0;
        var tripWrites = 0;
        var newPlace = Museum with { PlaceId = Guid.Parse("00000000-0000-0000-0000-000000009004") };
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/attractions")
            {
                if (request.Method == HttpMethod.Get)
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = restoreExistingPlace
                            ? JsonContent.Create(new[] { new { AttractionId = newPlace.PlaceId, newPlace.Name, newPlace.Address,
                                newPlace.Lat, newPlace.Lon, EstimatedVisitMinutes = 30 } })
                            : JsonContent.Create(Array.Empty<AttractionDto>())
                    };

                Assert.Equal(HttpMethod.Post, request.Method);
                catalogueWrites++;
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = JsonContent.Create(new
                    {
                        AttractionId = newPlace.PlaceId, newPlace.Name, newPlace.Address,
                        newPlace.Lat, newPlace.Lon, EstimatedVisitMinutes = 30
                    })
                };
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            Assert.Equal(newPlace.PlaceId, Assert.Single(body.Legs).To.PlaceId);
            return ++tripWrites == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json(Saved);
        });
        ConfigureBrowserDraftStorage();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Click(page, "Review trip");
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MoveMarker(1, 15, 10));
        Assert.Empty(page.FindAll(".trip-review"));

        if (restoreExistingPlace)
        {
            var moved = canvas.Instance.Stops[1];
            newPlace = moved with { PlaceId = newPlace.PlaceId };
            Click(page, "Save map draft");
            var snapshot = Assert.Single(JSInterop.Invocations["localStorage.setItem"]);
            JSInterop.Setup<string>("localStorage.getItem", call => Equals(call.Arguments[0], snapshot.Arguments[0]))
                .SetResult(Assert.IsType<string>(snapshot.Arguments[1]));
            JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(true);
            Click(page, "Restore map draft");
            Assert.Contains("Map draft restored from this browser.", page.Markup);
        }

        Click(page, "Review trip");
        Assert.Contains("They stay in the catalogue even if you discard this trip.", page.Find(".trip-review").TextContent);
        Assert.Equal(0, catalogueWrites);
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Equal(restoreExistingPlace ? 0 : 1, catalogueWrites));

        page.WaitForAssertion(() => Assert.Contains("entries are retained", page.Find("[role=alert]").TextContent));
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find(".trip-message.success").TextContent));
        Assert.Equal(2, tripWrites);
        Assert.Equal(restoreExistingPlace ? 0 : 1, catalogueWrites);
    }

    [Fact]
    public async Task LostPlaceResponseSavesTheTripAfterTheCreatedPlaceIsConfirmed()
    {
        var (page, writes) = await LoseCreateResponseAsync(committedRecordAppears: true);
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
        Assert.Equal(1, writes.Trip);
        Assert.Equal(1, writes.Catalogue);
    }

    [Fact]
    public async Task LostPlaceResponseBlocksTheTripSaveWhenNoRecordMatchesTheCreate()
    {
        var (page, writes) = await LoseCreateResponseAsync(committedRecordAppears: false);
        page.WaitForAssertion(() => Assert.Contains("No saved record matches Museum. The earlier save may not have applied", page.Find("[role=alert]").TextContent));
        Assert.DoesNotContain("nothing was created", page.Markup);
        Assert.DoesNotContain(page.FindAll(".trip-review button"), button => button.TextContent == "Save trip");
        Assert.Equal(0, writes.Trip);
        Assert.Equal(1, writes.Catalogue);
    }

    /** Counts the catalogue and trip writes a scenario has seen so far, read live by the assertions. */
    sealed class WriteCounts
    {
        public int Catalogue;

        public int Trip;
    }

    /**
     * Loses the first catalogue create response, then retries the catalogue save with or without the created record showing up in the lookup.
     * @param committedRecordAppears Whether the retry lookup returns the record the lost create made.
     * @return The rendered editor and the live catalogue and trip write counts.
     */
    async Task<(IRenderedComponent<NewTrip> Page, WriteCounts Writes)> LoseCreateResponseAsync(bool committedRecordAppears)
    {
        var previous = new AttractionDto(Guid.Parse("00000000-0000-0000-0000-000000009005"), Museum.Name, null, null, "", 45.52m, -73.59m, 30);
        var created = previous with { AttractionId = Guid.Parse("00000000-0000-0000-0000-000000009006"), Name = " MUSEUM " };
        var nearMatch = created with { AttractionId = Guid.Parse("00000000-0000-0000-0000-000000009007"), Lon = -73.58m };
        var writes = new WriteCounts();
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/attractions")
            {
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Contains("name=Museum", request.RequestUri.Query);
                    AttractionDto[] results = committedRecordAppears && writes.Catalogue > 0 ? [previous, nearMatch, created] : [previous];
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(results) };
                }

                Assert.Equal(HttpMethod.Post, request.Method);
                writes.Catalogue++;
                throw new HttpRequestException("Response lost after the create request.");
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            writes.Trip++;
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            Assert.Equal(created.AttractionId, Assert.Single(body.Legs).To.PlaceId);
            return Json(Saved);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, previous.Lat + 0.000003m, previous.Lon - 0.000004m)));

        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
        Click(page, "Save trip");
        return (page, writes);
    }

    [Fact]
    public void SignOutDropsTheSavedTripDraftSoTheNextAccountNeverSeesIt()
    {
        Configure(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(Saved);
            throw new InvalidOperationException("Unexpected request after the editor loaded.");
        }, withAccommodationCatalogue: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/new-trip/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.WaitForAssertion(() => Assert.Contains("Museum", page.Markup));

        ((SwitchableAuthentication)Services.GetRequiredService<AuthenticationStateProvider>()).SignOut();

        page.WaitForAssertion(() => Assert.DoesNotContain("Museum", page.Markup));
        Assert.DoesNotContain("Weekend", page.Markup);
        Assert.EndsWith("/new-trip", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public async Task CatalogueSaveRemainsSingleFlightAcrossTripRoutes()
    {
        var otherTrip = Saved with { TripId = Guid.Parse("00000000-0000-0000-0000-000000009008"), Name = "Other trip" };
        var catalogueWrites = 0;
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == $"/api/v1/trips/{TripId:D}")
                return Json(Saved);
            if (path == $"/api/v1/trips/{otherTrip.TripId:D}")
                return Json(otherTrip);

            Assert.Equal("/api/v1/attractions", path);
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<AttractionDto>()) };

            Assert.Equal(HttpMethod.Post, request.Method);
            catalogueWrites++;
            writeStarted.TrySetResult();
            return await reply.Task.WaitAsync(token);
        });
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(true);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/map/{TripId:D}");
        var routes = Render<Routes>();
        var page = routes.FindComponent<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.52m, -73.59m)));
        Click(page, "Review trip");
        var firstClick = page.Find(".trip-review button").TriggerEventAsync("onclick", new MouseEventArgs());

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await routes.InvokeAsync(() => navigation.NavigateTo($"/map/{otherTrip.TripId:D}"));
            page = routes.FindComponent<NewTrip>();
            canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
            await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.53m, -73.60m)));
            Click(page, "Review trip");
            var save = page.Find(".trip-review button");
            Assert.True(save.HasAttribute("disabled"));
            await save.TriggerEventAsync("onclick", new MouseEventArgs()).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, catalogueWrites);
        }
        finally
        {
            reply.TrySetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        await firstClick;
        page.WaitForAssertion(() => Assert.False(page.Find(".trip-review button").HasAttribute("disabled")));
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public async Task SaveIgnoresASecondClickUntilTheApiConfirms()
    {
        var writes = 0;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            writes++;
            started.TrySetResult(true);
            return await reply.Task.WaitAsync(token);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Click(page, "Review trip");
        var save = page.Find("[data-trip-save]");
        var firstClick = save.TriggerEventAsync("onclick", new MouseEventArgs());

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(page.Find("[data-trip-save]").HasAttribute("disabled"));
            Assert.DoesNotContain("Saved Weekend", page.Markup);
            await save.TriggerEventAsync("onclick", new MouseEventArgs()).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(1, writes);
        }
        finally
        {
            reply.TrySetResult(Json(Saved));
        }

        await firstClick;
        Assert.Contains("Saved Weekend", page.Markup);
    }

    [Fact]
    public void AccommodationCatalogueAddsTheSelectedPlaceToTheSharedMapDraft()
    {
        var accommodation = new AccommodationDto(Hotel.PlaceId, Hotel.Name, Hotel.Address,
            Hotel.Lat, Hotel.Lon, "private");
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            object body = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/accommodations" => new[] { accommodation },
                var path when path == $"/api/v1/accommodations/{accommodation.AccommodationId:D}" => accommodation,
                _ => throw new InvalidOperationException("Unexpected catalogue request.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }, withAccommodationCatalogue: true);
        var page = Render<NewTrip>();

        Click(page, "Browse accommodations");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find(".accommodation-card button").Click();
        page.Find(".accommodation-details button").Click();
        Assert.Equal(Hotel.Name, page.Find(".trip-stop strong").TextContent);
        Assert.Contains("Saved place selected for this trip draft", page.Find(".accommodation-details").TextContent);

        Click(page, "Map View");

        Assert.Empty(page.FindAll(".accommodation-workspace"));
        Assert.Single(page.FindAll(".trip-marker"));
        Assert.Equal($"Stop 1: {Hotel.Name}", page.Find(".trip-marker").GetAttribute("aria-label"));
    }

    [Fact]
    public void UndoKeepsOnlyTheTenMostRecentDraftEdits()
    {
        Configure((_, _) => throw new InvalidOperationException("Undo must not call a service."));
        var page = Render<NewTrip>();
        var undo = page.Find("[data-trip-undo]");
        var name = page.Find(".map-trip-name, .trip-basics input");

        Assert.True(undo.HasAttribute("disabled"));
        for (var edit = 1; edit <= 12; edit++)
            name.Change($"Trip {edit}");

        for (var edit = 11; edit >= 2; edit--)
        {
            undo.Click();
            Assert.Equal($"Trip {edit}", name.GetAttribute("value"));
        }
        Assert.True(undo.HasAttribute("disabled"));
    }

    [Fact]
    public async Task UndoRestoresDeletedStopsAndConnectionDetailsOneEditAtATime()
    {
        Configure((_, _) => throw new InvalidOperationException("Undo must not call a service."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.DropMarker("accommodation", 350, 200));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("attraction", 430, 220));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("transport_stop", 390, 290));
        Click(page, "Transport");
        Click(page, "Train");
        page.FindAll(".trip-stop")[0].Click();
        page.FindAll(".trip-stop")[2].Click();
        page.Find("input[aria-label='Duration hours']").Change("2");
        page.Find(".marker-remove").Click();

        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        Assert.Empty(page.FindAll(".route-line"));

        Click(page, "Undo last edit");
        Assert.Equal(3, page.FindAll(".trip-stop").Count);
        Assert.Single(page.FindAll(".route-line.train"));
        Assert.Equal("2", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));

        Click(page, "Undo last edit");
        Assert.Single(page.FindAll(".route-line.train"));
        Assert.Equal("", page.Find("input[aria-label='Duration hours']").GetAttribute("value") ?? "");

        Click(page, "Undo last edit");
        Assert.Empty(page.FindAll(".route-line"));
        Assert.Equal(new[] { "Accommodation 1", "Attraction 2", "Stop 3" },
            page.FindAll(".trip-stop strong").Select(name => name.TextContent));

        Click(page, "Undo last edit");
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
    }

    [Fact]
    public async Task RouterSwitchesBetweenMapAndNewTripWithoutLosingAnUnsavedDraft()
    {
        Configure((_, _) => throw new InvalidOperationException("Switching views must not call the catalogue."));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("new-trip");
        var routes = Render<Routes>();
        routes.Find(".map-trip-name, .trip-basics input").Change("Weekend");

        await NavigateUsingHeader("map");
        Assert.Single(routes.FindAll(".trip-map-canvas"));
        Assert.Equal("page", routes.Find("a[href='map']").GetAttribute("aria-current"));
        Assert.Equal("Weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));

        await NavigateUsingHeader("trip");
        Assert.Empty(routes.FindAll(".trip-map-canvas"));
        Assert.Equal("Weekend", routes.Find("h1").TextContent);
        Assert.Equal("page", routes.Find("a[href='trip']").GetAttribute("aria-current"));
        Assert.Equal("Weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));

        Click(routes.FindComponent<NewTrip>(), "Map View");
        Assert.EndsWith("/map", navigation.Uri);
        Assert.Single(routes.FindAll(".trip-map-canvas"));

        await NavigateUsingHeader("trip");
        Assert.EndsWith("/trip", navigation.Uri);
        Assert.Empty(routes.FindAll(".trip-map-canvas"));
        Assert.Empty(JSInterop.Invocations["confirmTripChanges"]);

        Task NavigateUsingHeader(string path) => routes.InvokeAsync(() =>
            navigation.NavigateTo(routes.Find($"a[href='{path}']").GetAttribute("href")!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingThePlannerWithUnsavedEditsRequiresConfirmation(bool discard)
    {
        Configure((_, _) => throw new InvalidOperationException("Leaving must not call a service."));
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(discard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/new-trip");
        var routes = Render<Routes>();

        Assert.False(routes.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        routes.Find(".map-trip-name, .trip-basics input").Change("Unsaved weekend");
        Assert.True(routes.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);

        await routes.InvokeAsync(() => navigation.NavigateTo("/"));

        Assert.Single(JSInterop.Invocations["confirmTripChanges"]);
        if (discard)
        {
            Assert.Equal(navigation.BaseUri, navigation.Uri);
            Assert.Empty(routes.FindComponents<NewTrip>());
        }
        else
        {
            Assert.EndsWith("/new-trip", navigation.Uri);
            Assert.Equal("Unsaved weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
        }
    }

    [Fact]
    public async Task DecliningADifferentSavedTripLinkKeepsTheCurrentDraft()
    {
        Configure((_, _) => throw new InvalidOperationException("A declined link must not load the trip."));
        JSInterop.Setup<bool>("confirmTripChanges", _ => true).SetResult(false);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/map");
        var routes = Render<Routes>();
        routes.Find(".map-trip-name, .trip-basics input").Change("Unsaved weekend");

        await routes.InvokeAsync(() => navigation.NavigateTo($"/map/{TripId:D}"));

        Assert.Single(JSInterop.Invocations["confirmTripChanges"]);
        Assert.EndsWith("/map", navigation.Uri);
        Assert.Equal("Unsaved weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public async Task ChangingMapQualityKeepsTheUnsavedDraftAndRemembersTheChoice()
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not write to a catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("attraction", 45.51m, -73.58m)));
        await page.InvokeAsync(() => canvas.Instance.MarkerDropped.InvokeAsync(("transport_stop", 45.52m, -73.59m)));
        var markers = page.FindAll(".trip-marker").Count;
        var stops = page.FindAll(".trip-stop").Count;

        page.Find("select[aria-label='Map quality']").Change("low");

        Assert.Equal("low", page.Find("select[aria-label='Map quality']").GetAttribute("value"));
        Assert.Equal(markers, page.FindAll(".trip-marker").Count);
        Assert.Equal(stops, page.FindAll(".trip-stop").Count);
        Assert.Contains(JSInterop.Invocations["localStorage.setItem"], call => Equals(call.Arguments[0], "traveltracker-map-quality-v1"));
    }

    [Fact]
    public void MapQualityDefaultsToAutomaticWhenNoChoiceIsSaved()
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not write to a catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();

        Assert.Equal("automatic", page.Find("select[aria-label='Map quality']").GetAttribute("value"));
    }

    [Fact]
    public async Task MapPickedStopsAndLegStayInTheDraftWhenSwitchingToStandardEditor()
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not write to a catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();

        Click(page, "Markers");
        Assert.NotEmpty(page.FindAll("[data-map-marker]"));
        Assert.All(page.FindAll("[data-map-marker]"), option => Assert.Equal("false", option.GetAttribute("aria-pressed")));
        Assert.Empty(page.FindAll(".location-target"));
        Click(page, "Accommodation");
        page.Find(".trip-place input[type=text]").Change("My hotel");
        page.Find("button[aria-label='Pick map center']").Click();
        page.Find(".trip-place").Submit();

        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.DropMarker("attraction", 580, 180));
        Assert.Equal("assets/trip-map/pin-attraction.svg", page.Find(".trip-marker[aria-pressed='true'] image").GetAttribute("href"));
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        page.Find(".trip-place input[type=text]").Change("My museum");
        page.Find(".trip-place").Submit();
        Click(page, "Stop");
        Assert.Equal("true", page.Find("button[data-map-marker='transport_stop']").GetAttribute("aria-pressed"));
        Assert.Contains("My museum", page.FindAll(".trip-stop")[1].TextContent);
        page.Find("button[aria-label='Pick map center']").Click();
        Assert.Equal(3, page.FindAll(".trip-stop").Count);
        Assert.Empty(page.FindAll(".route-line, .route-halo"));
        page.FindAll(".trip-stop")[2].Click();
        page.Find(".marker-remove").Click();
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        Assert.Empty(page.Find(".marker-panel").QuerySelectorAll("legend"));
        Click(page, "Transport");
        Click(page, "Walk");
        var card = page.Find(".leg-panel");
        Assert.Equal("Connect stops", card.QuerySelector("h2")!.TextContent);
        Assert.Empty(card.QuerySelectorAll("legend"));
        Assert.Contains("Walk", card.QuerySelector("output")!.TextContent);
        Assert.DoesNotContain("Standard editor", card.TextContent);
        page.FindAll(".trip-stop")[0].Click();
        Assert.Empty(page.FindAll(".route-line, .route-halo"));
        page.FindAll(".trip-stop")[1].Click();
        Assert.Single(page.FindAll(".route-line"));
        var originalPin = page.Find("[aria-label='Stop 2: My museum']").GetAttribute("transform");
        var originalLeg = page.Find(".route-line").GetAttribute("d");
        await page.InvokeAsync(() => canvas.Instance.MoveMarker(1, 15, 10));
        Assert.NotEqual(originalPin, page.Find("[aria-label='Stop 2: My museum']").GetAttribute("transform"));
        Assert.NotEqual(originalLeg, page.Find(".route-line").GetAttribute("d"));
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        NavigateToTrip(page);

        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        Assert.Contains("Walk", page.Find(".trip-leg-time").TextContent);
        Assert.Empty(page.FindAll(".trip-marker"));
        Click(page, "Review trip");
        Assert.Empty(page.FindAll(".trip-marker"));
        Assert.Single(page.FindAll(".review-legs li"));
        Assert.Contains("My museum", page.Find(".review-legs").TextContent);
        Assert.Contains("Enter a trip name", page.Find("[role=alert]").TextContent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapJoinsNonAdjacentStopsAndReturnsRegardlessOfConnectionOrder(bool addReturnFirst)
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not call the catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.DropMarker("accommodation", 350, 200));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("attraction", 430, 220));
        await page.InvokeAsync(() => canvas.Instance.DropMarker("transport_stop", 390, 290));
        Click(page, "Transport");

        if (addReturnFirst)
            Connect("Walk", "Attraction 2", "Accommodation 1");
        Connect("Train", "Accommodation 1", "Stop 3");
        Connect("Bus", "Stop 3", "Attraction 2");
        if (!addReturnFirst)
            Connect("Walk", "Attraction 2", "Accommodation 1");

        Assert.Equal(addReturnFirst
            ? new[] { "Attraction 2", "Accommodation 1", "Stop 3", "Attraction 2" }
            : new[] { "Accommodation 1", "Stop 3", "Attraction 2", "Accommodation 1" },
            page.FindAll(".trip-stop strong").Select(name => name.TextContent));
        Assert.Equal(addReturnFirst ? new[] { "Walk", "Train", "Bus" } : new[] { "Train", "Bus", "Walk" },
            page.FindAll(".map-leg-label span").Select(label => label.TextContent));
        Assert.Equal(3, page.FindAll(".route-line").Count);

        Click(page, "Review trip");
        Assert.Contains("Round trip", page.Find(".trip-review small").TextContent);

        void Connect(string mode, string from, string to)
        {
            Click(page, mode);
            page.Find($".trip-marker[aria-label$=': {from}']").Click();
            page.Find($".trip-marker[aria-label$=': {to}']").Click();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MapSavesReturnToTheSamePlaceWithItsOwnModeAndTimes(bool hasReturnStop)
    {
        var walk = new TripDto.Mode(Guid.Parse("00000000-0000-0000-0000-000000009009"), "pedestrian", "Walk");
        var returned = Saved with
        {
            IsRoundTrip = true,
            Legs = [Saved.Legs[0], new(2, Museum, Hotel, walk, Start.AddHours(3), Start.AddHours(4))]
        };
        SaveTripRequest? updated = null;
        Configure(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/transport-modes")
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { walk }) };
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);
            Assert.Equal(HttpMethod.Put, request.Method);
            updated = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(returned);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        if (hasReturnStop)
            Click(page, "+ Return to starting place");
        Click(page, "Transport");
        Click(page, "Walk");
        page.Find(".trip-marker[aria-label$=': Museum']").Click();
        page.Find(".trip-marker[aria-label$=': Museum']").Click();
        Assert.Single(page.FindAll(".route-line"));
        page.Find(".trip-marker[aria-label='Stop 1: Hotel']").Click();
        Assert.Equal(2, page.FindAll(".trip-marker").Count);
        Assert.Equal(3, page.FindAll(".trip-stop").Count);
        Assert.Empty(page.FindAll(".trip-stop-split"));

        var labels = page.FindAll(".map-leg-label");
        Assert.Equal(2, labels.Count);
        Assert.NotEqual(labels[0].GetAttribute("y"), labels[1].GetAttribute("y"));
        page.Find("button[aria-label^='Edit connection 2:']").Click();
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-14T12:00");
        page.Find("input[aria-label='Duration hours']").Change("1");

        Click(page, "Review trip");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
        Assert.NotNull(updated);
        Assert.True(updated.IsRoundTrip);
        Assert.Collection(updated.Legs,
            outbound =>
            {
                Assert.Equal(Hotel.PlaceId, outbound.From.PlaceId);
                Assert.Equal(Museum.PlaceId, outbound.To.PlaceId);
                Assert.Equal(Train.TransportModeId, outbound.TransportModeId);
                Assert.Equal(Start.AddHours(1), outbound.Departure);
                Assert.Equal(Start.AddHours(2), outbound.Arrival);
            },
            inbound =>
            {
                Assert.Equal(Museum.PlaceId, inbound.From.PlaceId);
                Assert.Equal(Hotel.PlaceId, inbound.To.PlaceId);
                Assert.Equal(walk.TransportModeId, inbound.TransportModeId);
                Assert.Equal(Start.AddHours(3), inbound.Departure);
                Assert.Equal(Start.AddHours(4), inbound.Arrival);
            });
    }

    [Fact]
    public async Task MovingStartingAccommodationUpdatesItsReturnVisitAndUndoRestoresBoth()
    {
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(Saved));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Click(page, "+ Return to starting place");
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        var changed = (Lat: Hotel.Lat + 0.01m, Lon: Hotel.Lon - 0.01m);
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((0, changed.Lat, changed.Lon)));

        var stops = canvas.Instance.Stops;
        Assert.Equal(changed, (stops[0].Lat, stops[0].Lon));
        Assert.Equal(stops[0], stops[^1]);
        Assert.NotEqual(stops[0], stops[1]);

        Click(page, "Undo last edit");
        Assert.Equal(Hotel, canvas.Instance.Stops[0]);
        Assert.Equal(Hotel, canvas.Instance.Stops[^1]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TravellerSavesMapTripUsingAnExistingMode(bool trainUnavailable, bool catalogueInitiallyEmpty)
    {
        SaveTripRequest? created = null;
        var walk = new TripDto.Mode(Guid.Parse("00000000-0000-0000-0000-000000009015"), "pedestrian", "Walk");
        var expectedMode = trainUnavailable && !catalogueInitiallyEmpty ? walk : Train;
        var catalogueReads = 0;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            switch (path)
            {
                case "/api/v1/accommodations":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AccommodationId = Hotel.PlaceId, Hotel.Name, Hotel.Address, Hotel.Lat, Hotel.Lon } }) };
                case "/api/v1/attractions":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AttractionId = Museum.PlaceId, Museum.Name, Museum.Address, Museum.Lat, Museum.Lon, EstimatedVisitMinutes = 30 } }) };
                case "/api/v1/transport-modes":
                    Assert.Equal(HttpMethod.Get, request.Method);
                    var modes = catalogueInitiallyEmpty && catalogueReads++ == 0 ? Array.Empty<TripDto.Mode>() : [expectedMode];
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(modes) };
                case "/api/v1/trips":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
                    return Json(Saved with { Legs = [Saved.Legs[0] with { Transport = expectedMode }] });
                default:
                    throw new InvalidOperationException($"Unexpected request path {path}.");
            }
        }, scopes: "catalogue.read catalogue.write trips.read trips.write");
        ConfigureBrowserDraftStorage();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/map");
        var page = Render<NewTrip>();

        page.Find(".map-trip-name, .trip-basics input").Change("Weekend");
        SelectTripDates(page, new(2026, 11, 14), new(2026, 11, 14));
        Click(page, "Markers");
        Click(page, "Accommodation");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Hotel", page.Find(".trip-search-results").TextContent));
        Click(page, "Hotel");
        Click(page, "Attraction");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Museum", page.Find(".trip-search-results").TextContent));
        Click(page, "Museum");
        Click(page, "Transport");
        Click(page, "Train");
        page.FindAll(".trip-stop")[0].Click();
        page.FindAll(".trip-stop")[1].Click();
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.Find("input[aria-label='Duration hours']").Change("1");
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-14T10:00:00");
        Assert.Contains("Train · 1 h 0 min", page.Find(".map-leg-label").TextContent);
        Assert.StartsWith("2026-11-14T11:00", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));

        page.Find("input[aria-label='Duration minutes']").Change("60");
        Assert.Contains("Enter whole minutes from 0 to 59.", page.Find("[role=alert]").TextContent);
        page.Find("input[aria-label='Duration minutes']").Change("20");
        Assert.StartsWith("2026-11-14T11:20", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));

        page.FindAll(".leg-panel input[type=datetime-local]")[1].Change("2026-11-14T11:15");
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("15", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.Contains("Train · 1 h 15 min", page.Find(".map-leg-label").TextContent);
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-14T10:15");
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("0", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.StartsWith("2026-11-14T11:15", page.FindAll(".leg-panel input[type=datetime-local]")[1].GetAttribute("value"));
        Click(page, "Review trip");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save trip");
        if (trainUnavailable)
        {
            page.WaitForAssertion(() => Assert.Contains("Transport modes unavailable in the catalogue: Train", page.Find("[role=alert]").TextContent));
            Assert.Null(created);
            Click(page, "Back to stops");
            Assert.DoesNotContain(page.FindAll(".trip-palette button"), button => button.TextContent.Trim() == "Train");
            if (catalogueInitiallyEmpty)
            {
                Assert.Contains("No transport modes are available", page.Markup);
            }
            else
            {
                Click(page, "Walk");
                page.FindAll(".trip-stop")[0].Click();
                page.FindAll(".trip-stop")[1].Click();
            }
            Click(page, "Review trip");
            Click(page, "Save trip");
        }

        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[role=status]"), item => item.TextContent.Contains("Saved Weekend")));
        Assert.Collection(JSInterop.Invocations["localStorage.setItem"],
            item => Assert.EndsWith("-new", Assert.IsType<string>(item.Arguments[0])),
            item => Assert.EndsWith($"-{TripId:D}", Assert.IsType<string>(item.Arguments[0])));
        Assert.NotNull(created);
        Assert.Equal(Start.AddHours(-9), created.Start);
        Assert.Equal(Start.AddHours(-9).AddDays(1).AddTicks(-1), created.End);
        var leg = Assert.Single(created.Legs);
        Assert.Equal(Hotel.PlaceId, leg.From.PlaceId);
        Assert.Equal(Museum.PlaceId, leg.To.PlaceId);
        Assert.Equal(expectedMode.TransportModeId, leg.TransportModeId);
        Assert.Equal(Start.AddMinutes(75), leg.Departure);
        Assert.Equal(Start.AddMinutes(135), leg.Arrival);
        Assert.EndsWith($"/map/{TripId:D}", navigation.Uri);
    }

    [Fact]
    public void MapConnectionDurationUsesElapsedTimeAcrossDaylightSaving()
    {
        var departure = DateTimeOffset.Parse("2026-03-08T01:30:00-05:00");
        var arrival = DateTimeOffset.Parse("2026-03-08T03:30:00-04:00");
        var trip = Saved with
        {
            Start = departure.AddHours(-1),
            End = arrival.AddHours(1),
            Legs = [new(1, Hotel, Museum, Train, departure, arrival)]
        };
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(trip));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        page.Find("button[aria-label^='Edit connection 1:']").Click();
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("0", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.Contains("Train · 1 h 0 min", page.Find(".map-leg-label").TextContent);

        page.FindAll(".leg-panel input[type=datetime-local]")[1].Change("2026-03-08T04:00");
        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Assert.Equal("30", page.Find("input[aria-label='Duration minutes']").GetAttribute("value"));
        Assert.Contains("Train · 1 h 30 min", page.Find(".map-leg-label").TextContent);
    }

    [Fact]
    public void RepeatedLocalTimesSaveWithTheirExplicitOffsets()
    {
        var trip = Saved with
        {
            Start = DateTimeOffset.Parse("2026-11-01T00:30:00-04:00"),
            End = DateTimeOffset.Parse("2026-11-01T02:30:00-05:00"),
            Legs = [new(1, Hotel, Museum, Train, DateTimeOffset.Parse("2026-11-01T00:45:00-04:00"),
                DateTimeOffset.Parse("2026-11-01T02:00:00-05:00"))]
        };
        SaveTripRequest? updated = null;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
                return Json(trip);

            Assert.Equal(HttpMethod.Put, request.Method);
            updated = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(trip, "\"2\"");
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        SelectTripDates(page, new(2026, 11, 1), new(2026, 11, 1));
        Assert.Empty(page.FindAll(".trip-basics select"));

        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-01T01:30");
        page.FindAll(".leg-panel input[type=datetime-local]")[1].Change("2026-11-01T01:30");
        page.FindAll(".leg-fields select")[0].Change("-04:00:00");
        page.FindAll(".leg-fields select")[1].Change("-05:00:00");

        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Click(page, "Review trip");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
        Assert.NotNull(updated);
        Assert.Equal(TimeSpan.FromHours(-4), updated.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), updated.End.Offset);
        var leg = Assert.Single(updated.Legs);
        Assert.Equal(TimeSpan.FromHours(-4), leg.Departure.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), leg.Arrival.Offset);
        Assert.Equal(TimeSpan.FromHours(1), leg.Arrival - leg.Departure);
    }

    [Fact]
    public void StopAndAccommodationDurationsFollowTheirAdjacentConnections()
    {
        var station = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009010"), "transport_stop", "Station", "", 45.52m, -73.59m);
        var overnight = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009011"), "accommodation", "Overnight hotel", "", 45.53m, -73.60m);
        var trip = Saved with
        {
            Start = DateTimeOffset.Parse("2026-03-08T00:00:00-05:00"),
            End = DateTimeOffset.Parse("2026-03-08T08:00:00-04:00"),
            Legs =
            [
                new(1, Hotel, station, Train, DateTimeOffset.Parse("2026-03-08T01:00:00-05:00"),
                    DateTimeOffset.Parse("2026-03-08T01:30:00-05:00")),
                new(2, station, overnight, Train, DateTimeOffset.Parse("2026-03-08T03:30:00-04:00"),
                    DateTimeOffset.Parse("2026-03-08T04:00:00-04:00")),
                new(3, overnight, Museum, Train, DateTimeOffset.Parse("2026-03-08T06:15:00-04:00"),
                    DateTimeOffset.Parse("2026-03-08T07:00:00-04:00"))
            ]
        };
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(trip));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        string? DurationAt(int index) =>
            page.FindAll(".trip-stop")[index].QuerySelector(".trip-stop-duration")?.TextContent.Trim();

        Assert.Null(DurationAt(0));
        Assert.Equal("Time at stop · 1 h 0 min", DurationAt(1));
        Assert.Equal("Stay · 2 h 15 min", DurationAt(2));
        Assert.Null(DurationAt(3));

        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.Find("input[aria-label='Duration minutes']").Change("45");
        Assert.Equal("Time at stop · 45 min", DurationAt(1));

        page.Find("button[aria-label^='Edit connection 2:']").Click();
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-03-08T01:45");
        Assert.Equal("Time at stop · 0 min", DurationAt(1));
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-03-08T01:40");
        Assert.Null(DurationAt(1));

        page.Find("button[aria-label^='Edit connection 3:']").Click();
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-03-08T05:30");
        Assert.Equal("Stay · 1 h 30 min", DurationAt(2));
    }

    [Fact]
    public void MapShowsAdjacentStopsAtTheSameCoordinatesInASplitCard()
    {
        var station = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009012"), "transport_stop", "Station", "", Hotel.Lat, Hotel.Lon);
        var walk = new TripDto.Mode(Guid.Parse("00000000-0000-0000-0000-000000009013"), "pedestrian", "Walk");
        var trip = Saved with
        {
            Legs =
            [
                new(1, Hotel, station, walk, Start, Start.AddMinutes(10)),
                new(2, station, Museum, Train, Start.AddMinutes(10), Start.AddHours(2))
            ]
        };
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(trip));
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        var cards = page.FindAll(".trip-stop-split .trip-stop");
        Assert.Equal(new[] { "Hotel", "Station" }, cards.Select(card => card.QuerySelector("strong")!.TextContent));
        Assert.Contains("09:00", cards[0].TextContent);
        Assert.Contains("09:10", cards[1].TextContent);
        Assert.Contains("10 min", page.Find(".trip-local-connection").TextContent);
        Assert.Equal(2, page.FindAll(".trip-marker").Count);

        cards[1].Click();
        Assert.Equal("true", page.Find(".trip-marker[aria-label='Stop 2: Station']").GetAttribute("aria-pressed"));
        Assert.Equal(2, page.FindAll(".trip-marker").Count);

        Click(page, "Review trip");
        Assert.Equal(2, page.FindAll(".review-legs li").Count);
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public async Task MapPageServesItsFigmaIconsAndUndoShortcut()
    {
        await using var factory = new FrontendFactory();
        using var browser = factory.CreateBrowser();
        var page = await browser.GetStringAsync("/map");
        var links = System.Text.RegularExpressions.Regex.Matches(page, @"assets/trip-map/[a-z0-9_.-]+\.svg");
        Assert.Equal(5, links.Select(link => link.Value).Distinct().Count());
        var urls = links.Select(link => link.Value).Concat(new[]
        {
            "assets/trip-map/bike.svg",
            "assets/trip-map/pin-attraction.svg",
            "assets/trip-map/pin-accommodation.svg",
            "assets/trip-map/pin-transport_stop.svg"
        });
        foreach (var url in urls.Distinct())
        {
            using var response = await browser.GetAsync("/" + url);
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("<svg", await response.Content.ReadAsStringAsync());
        }
        Assert.Contains("export function watchUndo", await browser.GetStringAsync("/Components/Pages/NewTrip.razor.js"));
    }

    [Fact]
    public async Task RepeatedMarkerMovesLookUpEachFinishedPositionAndKeepSuggestionsUnconfirmed()
    {
        var lookups = 0;
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath.StartsWith("/v1/places/"))
            {
                lookups++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AddressLookupResult("success",
                        [new($"Suggested address {lookups}", 46m, -74m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
                });
            }
            return Task.FromResult(Json(Saved));
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.52m, -73.59m)));
        page.WaitForAssertion(() => Assert.Equal("Suggested address 1", page.Find(".address-search input").GetAttribute("value")));
        Assert.Equal("", canvas.Instance.Stops[1].Address);
        Assert.Equal(45.52m, canvas.Instance.Stops[1].Lat);
        page.Find(".trip-place input[type=text]").Change("My pending marker name");
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.53m, -73.60m)));
        page.WaitForAssertion(() => Assert.Equal("Suggested address 2", page.Find(".address-search input").GetAttribute("value")));
        Assert.Equal("My pending marker name", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(2, lookups);
        Assert.Equal("", canvas.Instance.Stops[1].Address);
        Assert.Equal(45.53m, canvas.Instance.Stops[1].Lat);
        page.Find(".trip-place").Submit();
        Assert.Equal("Suggested address 2", canvas.Instance.Stops[1].Address);
        Assert.Equal("My pending marker name", canvas.Instance.Stops[1].Name);
        Assert.Equal(45.53m, canvas.Instance.Stops[1].Lat);
        Assert.Equal(2, canvas.Instance.Stops.Count);
    }

    [Fact]
    public async Task SelectedAddressPreviewsItsPositionUntilApplyWithoutCreatingADuplicateStop()
    {
        var lookups = 0;
        Configure((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath == "/v1/places/search")
            {
                lookups++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AddressLookupResult("success",
                        [new("Chosen street", 45.53m, -73.60m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
                });
            }
            return Task.FromResult(Json(Saved));
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        var search = page.Find(".address-search");
        Assert.Null(search.ParentElement!.Closest("form"));
        page.Find(".address-search input").Input("Chosen street");
        Assert.Equal(0, lookups);
        search.Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".address-candidates button")));
        page.Find(".address-candidates button").Click();
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        Assert.Equal((45.53m, -73.60m), canvas.Instance.PreviewLocation);
        Assert.Single(page.FindAll(".address-preview-marker"));
        Assert.Equal(2, canvas.Instance.Stops.Count);
        page.Find(".trip-place").Submit();
        Assert.Equal("Chosen street", canvas.Instance.Stops[1].Address);
        Assert.Equal(45.53m, canvas.Instance.Stops[1].Lat);
        Assert.Equal(2, canvas.Instance.Stops.Count);
        Assert.Equal(1, lookups);
    }

    [Fact]
    public async Task CancellingAChosenAddressPreviewPreservesTheExistingStopAndItsCoordinates()
    {
        Configure((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/places/search"
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new AddressLookupResult("success",
                    [new("Unconfirmed street", 45.53m, -73.60m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
            }
            : Json(Saved)), routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".address-search input").Input("Unconfirmed street");
        page.Find(".address-search").Submit();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".address-candidates button")));
        page.Find(".address-candidates button").Click();
        Assert.Single(page.FindAll(".address-preview-marker"));

        page.Find(".trip-place button[type=button]").Click();

        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        Assert.Equal(2, canvas.Instance.Stops.Count);
        Assert.Empty(page.FindAll(".address-preview-marker"));
        Assert.Null(canvas.Instance.PreviewLocation);
    }

    [Fact]
    public async Task StandardTripEditorCanChooseAMapPositionWithoutTypingCoordinates()
    {
        Configure((_, _) => throw new InvalidOperationException("Choosing a map position must not save a catalogue record."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/trip");
        var page = Render<NewTrip>();
        Click(page, "Attraction");
        Click(page, "Enter a location");
        var map = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        Assert.Empty(map.Instance.Stops);
        page.Find(".trip-place input[type=text]").Change("Map-picked museum");
        await page.InvokeAsync(() => map.Instance.LocationPicked.InvokeAsync((45.51m, -73.59m)));
        Assert.Empty(page.FindAll(".trip-stop"));
        page.Find(".trip-place").Submit();
        Assert.Single(page.FindAll(".trip-stop"));
        Assert.Contains("Map-picked museum", page.Markup);
        Assert.Empty(page.FindAll(".trip-place input[type=number][required]"));
    }

    [Theory]
    [InlineData("America/Toronto")]
    [InlineData("Eastern Standard Time")]
    public async Task ConfirmedRouteAppliesExactSecondsSupportsUndoAndPersistsWithoutLosingSessionGeometry(string timeZone)
    {
        var original = Saved with { TimeZone = timeZone };
        SaveTripRequest? savedRequest = null;
        var catalogueWrites = 0;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities") return RoutingFixtures.Json(RoutingFixtures.Capabilities);
            if (path == "/v1/routes/plan")
            {
                var query = (await request.Content!.ReadFromJsonAsync<RoutePlanRequest>(token))!;
                Assert.DoesNotContain("Standard Time", query.TimeZone);
                return RoutingFixtures.Json(RoutingFixtures.Success(query));
            }
            if (path.StartsWith("/api/v1/transport-modes"))
            {
                if (request.Method != HttpMethod.Get) catalogueWrites++;
                return path.EndsWith(Train.TransportModeId.ToString(), StringComparison.Ordinal)
                    ? RoutingFixtures.Json(RoutingFixtures.Train) : RoutingFixtures.Json(new[] { RoutingFixtures.Train });
            }
            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get) return Json(original);
            savedRequest = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            var leg = Assert.Single(savedRequest.Legs);
            return Json(original with { Legs = [original.Legs[0] with { Departure = leg.Departure, Arrival = leg.Arrival, EncodedPolyline = leg.EncodedPolyline }] });
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        Assert.Equal(2, page.FindAll(".provider-route").Count);
        Assert.StartsWith("2026-11-14T10:00", page.FindAll(".leg-fields input[type=datetime-local]")[0].GetAttribute("value"));
        Assert.Null(savedRequest);
        Click(page, "Cancel route preview");
        Assert.Empty(page.FindAll(".provider-route"));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        Click(page, "Confirm selected route");
        page.WaitForAssertion(() => Assert.Contains("Selected route applied", page.Markup));
        Assert.Equal(RoutingFixtures.Departure, page.FindComponent<TravelTracker.Frontend.Components.Trips.RoutePlannerPanel>().Instance.InitialRequest!.DepartureTime);
        Assert.Contains("10:07:37", page.Find(".trip-message.success").TextContent);
        Assert.Single(page.FindAll(".confirmed-route"));
        Assert.Empty(page.FindAll(".route-line:not(.provider-route)"));
        page.Find("[data-trip-undo]").Click();
        Assert.StartsWith("2026-11-14T10:00", page.FindAll(".leg-fields input[type=datetime-local]")[0].GetAttribute("value"));
        Assert.Empty(page.FindAll(".provider-route"));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        Click(page, "Confirm selected route");
        page.WaitForAssertion(() => Assert.Contains("Selected route applied", page.Markup));
        Click(page, "Review trip");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.NotNull(savedRequest));
        Assert.Equal(RoutingFixtures.Departure, Assert.Single(savedRequest!.Legs).Departure);
        Assert.Equal(RoutingFixtures.Arrival, savedRequest.Legs[0].Arrival);
        Assert.Equal(RoutingFixtures.Success().Itineraries[0].Legs.SelectMany(section => RouteGeometry.Decode(section.Geometry)),
            RouteGeometry.Decode(savedRequest.Legs[0].EncodedPolyline!));
        Assert.Equal(timeZone, savedRequest.TimeZone);
        Assert.Equal(Train.TransportModeId, savedRequest.Legs[0].TransportModeId);
        Assert.Equal(0, catalogueWrites);
        Assert.Single(page.FindAll(".confirmed-route"));
        Assert.Empty(page.FindAll(".route-line:not(.provider-route)"));
        Assert.Equal(new[] { "Hotel", "Museum" }, page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>().Instance.Stops.Select(stop => stop.Name));
    }

    [Fact]
    public void ReplacingAndDeletingAConnectionPreservesBothStopsAndSupportsUndo()
    {
        Configure((request, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Click(page, "Edit transport");
        page.Find("[aria-label='Connection transport']").Change("bus");
        Assert.Single(page.FindAll(".route-line.bus"));
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        page.Find("[aria-label='Transport palette'] button").Click();
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        Assert.Single(page.FindAll(".route-line.pedestrian"));
        page.Find("[data-trip-delete=connection]").Click();
        Assert.Empty(page.FindAll(".route-line"));
        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        page.Find("[data-trip-undo]").Click();
        Assert.Single(page.FindAll(".route-line.pedestrian"));
        page.FindAll(".trip-stop")[0].Click();
        page.Find("[data-trip-delete=stop]").Click();
        Assert.Single(page.FindAll(".trip-stop"));
    }

    [Fact]
    public void RouterUnsupportedBusSavesAsAManualConnectionWithTheEnteredTimes()
    {
        var bus = RoutingFixtures.Train with { Code = "bus", Name = "City bus", TransportModeId = Guid.NewGuid() };
        SaveTripRequest? saved = null;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities")
                return RoutingFixtures.Json(RoutingFixtures.Capabilities with { Modes = [new("bus", "unsupported")] });
            if (path.StartsWith("/api/v1/transport-modes")) return RoutingFixtures.Json(new[] { bus });
            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Put) saved = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
            return Json(Saved);
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Click(page, "Edit transport");
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=bus]").HasAttribute("disabled")));
        page.Find("#route-mode").Change("bus");
        Assert.True(page.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
        Click(page, "Use manual connection");
        page.Find("[data-trip-save]").Click();
        page.WaitForAssertion(() => Assert.NotNull(saved));
        var leg = Assert.Single(saved!.Legs);
        Assert.Equal(bus.TransportModeId, leg.TransportModeId);
        Assert.Equal(Saved.Legs[0].Departure, leg.Departure);
        Assert.Equal(Saved.Legs[0].Arrival, leg.Arrival);
        Assert.Null(leg.EncodedPolyline);
    }

    [Fact]
    public void ConnectionTransportRemainsPreselectedWhenItsDepartureIsCleared()
    {
        var metadataReads = 0;
        Configure((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities")
            {
                metadataReads++;
                return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Capabilities));
            }
            if (path.StartsWith("/api/v1/transport-modes"))
                return Task.FromResult(RoutingFixtures.Json(new[] { RoutingFixtures.Train }));
            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            return Task.FromResult(Json(Saved));
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=train]").HasAttribute("disabled")));

        page.FindAll(".leg-fields input[type=datetime-local]")[0].Change("");

        Assert.Null(page.FindComponent<TravelTracker.Frontend.Components.Trips.RoutePlannerPanel>().Instance.InitialRequest);
        Assert.Equal("train", page.Find("#route-mode").GetAttribute("value"));
        Assert.Equal(1, metadataReads);
        Assert.True(page.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(".leg-fields input[type=datetime-local]", 0)]
    [InlineData(".leg-fields input[type=datetime-local]", 1)]
    [InlineData("input[aria-label='Duration hours']", 0)]
    [InlineData("input[aria-label='Duration minutes']", 0)]
    public void KeyboardSaveFromAnUnchangedConnectionFieldPreservesConfirmedRouteTimes(string selector, int index)
    {
        SaveTripRequest? savedRequest = null;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities") return RoutingFixtures.Json(RoutingFixtures.Capabilities);
            if (path == "/v1/routes/plan") return RoutingFixtures.Json(RoutingFixtures.Success());
            if (path.StartsWith("/api/v1/transport-modes"))
                return path.EndsWith(Train.TransportModeId.ToString(), StringComparison.Ordinal)
                    ? RoutingFixtures.Json(RoutingFixtures.Train) : RoutingFixtures.Json(new[] { RoutingFixtures.Train });
            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get) return Json(Saved);
            savedRequest = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            var leg = Assert.Single(savedRequest.Legs);
            return Json(Saved with { Legs = [Saved.Legs[0] with { Departure = leg.Departure, Arrival = leg.Arrival, EncodedPolyline = leg.EncodedPolyline }] });
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        Click(page, "Confirm selected route");
        page.WaitForAssertion(() => Assert.Contains("Selected route applied", page.Markup));

        // The save shortcut commits the focused field with change before clicking the workspace save button.
        var focused = page.FindAll(selector)[index];
        focused.Change(focused.GetAttribute("value"));
        page.Find("[data-trip-save]").Click();

        page.WaitForAssertion(() => Assert.NotNull(savedRequest));
        Assert.Equal(RoutingFixtures.Departure, savedRequest!.Legs[0].Departure);
        Assert.Equal(RoutingFixtures.Arrival, savedRequest.Legs[0].Arrival);
        Assert.Single(page.FindAll(".confirmed-route"));
        Assert.Empty(page.FindAll(".route-line:not(.provider-route)"));
    }

    [Fact]
    public async Task ClearingTheSelectionDuringTransportConfirmationPreservesTheConnection()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Configure((request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities") return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Capabilities));
            if (path == "/v1/routes/plan") return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Success()));
            if (path.StartsWith("/api/v1/transport-modes"))
            {
                if (path.EndsWith(Train.TransportModeId.ToString(), StringComparison.Ordinal))
                {
                    started.TrySetResult();
                    return reply.Task.WaitAsync(token);
                }
                return Task.FromResult(RoutingFixtures.Json(new[] { RoutingFixtures.Train }));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(Saved));
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        var confirmation = page.FindAll("button").Single(button => button.TextContent == "Confirm selected route")
            .TriggerEventAsync("onclick", new MouseEventArgs());
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            // Model an invalidation delivered while the asynchronous catalogue check is pending.
            await page.InvokeAsync(() => page.FindComponent<TravelTracker.Frontend.Components.Trips.RoutePlannerPanel>()
                .Instance.PreviewChanged.InvokeAsync(null));
        }
        finally
        {
            reply.TrySetResult(RoutingFixtures.Json(RoutingFixtures.Train));
        }
        await confirmation;

        Assert.Contains("route was not applied", page.Markup);
        Assert.Equal(Saved.Legs[0].Departure, page.FindComponent<TravelTracker.Frontend.Components.Trips.RoutePlannerPanel>()
            .Instance.InitialRequest!.DepartureTime);
        Assert.Empty(page.FindAll(".provider-route"));
        Assert.False(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    [Fact]
    public void WrongSavedModeIdWithTheSameCodeCannotReplaceTheSelectedModeDuringConfirmation()
    {
        var detailReads = 0;
        Configure((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/routes/capabilities") return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Capabilities));
            if (path == "/v1/routes/plan") return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Success()));
            if (path.StartsWith("/api/v1/transport-modes"))
            {
                if (path.EndsWith(Train.TransportModeId.ToString(), StringComparison.Ordinal))
                {
                    detailReads++;
                    return Task.FromResult(RoutingFixtures.Json(RoutingFixtures.Train with { TransportModeId = Guid.NewGuid() }));
                }
                return Task.FromResult(RoutingFixtures.Json(new[] { RoutingFixtures.Train }));
            }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json(Saved));
        }, routing: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.WaitForAssertion(() => Assert.False(page.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Click(page, "Find routes");
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".route-options > li")));
        page.Find(".route-options > li > button").Click();
        Click(page, "Confirm selected route");
        page.WaitForAssertion(() => Assert.Equal(1, detailReads));
        Assert.Contains("route was not applied", page.Markup);
        Assert.StartsWith("2026-11-14T10:00", page.FindAll(".leg-fields input[type=datetime-local]")[0].GetAttribute("value"));
        Assert.StartsWith("2026-11-14T11:00", page.FindAll(".leg-fields input[type=datetime-local]")[1].GetAttribute("value"));
        Assert.False(page.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
    }

    void ConfigureBrowserDraftStorage(bool signedIn = true)
    {
        var authentication = new SwitchableAuthentication(new SessionRegistry(TimeProvider.System));
        if (signedIn)
        {
            authentication.SignIn("test-token", DateTimeOffset.UtcNow.AddHours(1));
            var identity = (ClaimsIdentity)authentication.User.Identity!;
            identity.AddClaim(new Claim("iss", "https://traveltracker.test/"));
            identity.AddClaim(new Claim("sub", "auth0|test-user"));
        }
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<ProtectedSessionStorage>();
    }

    void Configure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        bool withAccommodationCatalogue = false, string? scopes = null, bool routing = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clock = new FakeTimeProvider(Start);
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", Start.AddHours(1),
            scopes ?? (withAccommodationCatalogue ? "catalogue.read" : "users.read users.write"));
        Services.AddSingleton<IDataProtectionProvider, EphemeralDataProtectionProvider>();
        Services.AddScoped<ProtectedLocalStorage>();
        Services.AddScoped<MapQualityPreference>();
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var directory = new ServiceDirectory(new Dictionary<string, string>
        {
            ["Trips"] = "http://localhost:5108",
            ["Accommodations"] = "http://localhost:5106",
            ["Attractions"] = "http://localhost:5104",
            ["Transport"] = "http://localhost:5105",
            ["Routing"] = "https://routing.example"
        });
        var services = new ServiceClient(new HttpClient(new StubHandler(respond)), NullLogger<ServiceClient>.Instance);
        var client = new ProtectedClient(authentication, directory, services, clock, NullLogger<ProtectedClient>.Instance, sessions);
        if (routing) Services.AddSingleton(new RoutingClient(client));
        var transport = new TransportClient(client);
        Services.AddSingleton(transport);
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new TripPlacesClient(new AttractionsClient(client), transport, new AccommodationsClient(client)));
        if (withAccommodationCatalogue)
        {
            Services.AddSingleton<AuthenticationStateProvider>(authentication);
            Services.AddSingleton<TimeProvider>(clock);
            Services.AddSingleton(sessions);
            Services.AddSingleton(new AccommodationsClient(client));
            Services.AddSingleton(new UsersClient(client, new ConfigurationBuilder().Build()));
        }
    }

    static HttpResponseMessage Json(TripDto value, string etag = "\"1\"") => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(value),
        Headers = { ETag = new(etag) }
    };

    /** Uses the public Trip route, preserving the current saved-trip identifier. */
    void NavigateToTrip(IRenderedComponent<NewTrip> page)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.Uri.Replace("/map", "/trip", StringComparison.Ordinal));
        page.Render();
    }

    /** Supplies a complete date range when a test is concerned with another planner behavior. */
    static void SelectTripDates(IRenderedComponent<NewTrip> page, DateOnly start, DateOnly end) =>
        page.InvokeAsync(() => page.FindComponent<TravelTracker.Frontend.Components.Trips.TripDateRangePicker>().Instance.RangeChanged.InvokeAsync((start, end))).GetAwaiter().GetResult();

    static void Click(IRenderedComponent<NewTrip> page, string label) =>
        (label is "Save trip" or "Retry save" ? page.Find("[data-trip-save]")
            : page.FindAll("button").Single(button => button.TextContent.Trim() == label)).Click();
}
