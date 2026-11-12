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
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies the shared draft and the Trips HTTP contract through rendered planner controls. */
public sealed class TripPlannerTests : BunitContext
{
    static readonly Guid TripId = Guid.Parse("a4fc96df-c35b-4305-92d8-4ae86638b585");
    static readonly TripPlaceDto Hotel = new(Guid.NewGuid(), "accommodation", "Hotel", "", 45.50m, -73.57m);
    static readonly TripPlaceDto Museum = new(Guid.NewGuid(), "attraction", "Museum", "", 45.51m, -73.58m);
    static readonly TripDto.Mode Train = new(Guid.NewGuid(), "train", "Train");
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-14T09:00:00-05:00");
    static readonly TripDto Saved = new(TripId, "Weekend", "America/Toronto", Start, Start.AddHours(4),
        false, [new(1, Hotel, Museum, Train, Start.AddHours(1), Start.AddHours(2))]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateNewFromTripWorkspaceHandlesUnsavedChangesBeforeReplacingDraft(bool discard)
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(discard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/trip/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        page.Find(".trip-basics input").Change("Unsaved title");
        Assert.Single(page.FindAll("button"), button => button.TextContent == "Open saved");

        Click(page, "Create new");

        Assert.Single(JSInterop.Invocations["confirm"]);
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
        Assert.Empty(JSInterop.Invocations["confirm"]);
        Click(page, "Open saved");
        Assert.Empty(page.FindAll("#saved-trip-browser"));
        Assert.Equal("Keep my draft", page.Find(".trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public async Task MapToolsTogglePreservesSelectedStopAndUnconfirmedEditBuffer()
    {
        Configure((_, _) => Task.FromResult(Json(Saved)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        Assert.DoesNotContain(page.FindAll("button"), button => button.TextContent.Trim() is "Basics" or "Stops");
        Assert.Single(page.FindAll(".trip-chain .trip-basics"));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopSelected.InvokeAsync(1));
        page.Find(".trip-place input[type=text]").Change("Pending name");
        Click(page, "Map tools");
        Assert.Equal("false", page.Find("[aria-controls=trip-tools-panel]").GetAttribute("aria-expanded"));
        Assert.True(page.Find("#trip-tools-panel").HasAttribute("inert"));
        Assert.Contains("tools-collapsed", page.Find("#trip-tools-panel").ClassName);
        Click(page, "Map tools");
        Assert.Equal("Pending name", page.Find(".trip-place input[type=text]").GetAttribute("value"));
        Assert.Equal(1, canvas.Instance.SelectedIndex);
        Assert.Equal(Museum, canvas.Instance.Stops[1]);
        Assert.False(page.Find("#trip-tools-panel").HasAttribute("inert"));
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
        page.FindAll(".trip-place input[type=text]")[1].Change("New address");
        page.Find(".trip-place").Submit();
        Assert.Equal(Guid.Empty, canvas.Instance.Stops[1].PlaceId);
        Assert.Equal(name, page.FindAll(".trip-stop strong")[1].TextContent);
        Assert.Empty(page.FindAll("script"));
        Click(page, "Review on map");
        Click(page, "Save catalogue entries");
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
        var inputs = page.FindAll(".trip-basics input[type=datetime-local]");
        Assert.All(inputs, input => Assert.Equal("60", input.GetAttribute("step")));
        Assert.Equal("2026-11-14T09:00", inputs[0].GetAttribute("value"));
        page.Find(".map-trip-name, .trip-basics input").Change("Renamed trip");
        Click(page, "Review on map");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Single(writes));
        Assert.Equal(instant, writes[0].Start);
        Assert.Equal(instant.AddHours(1), writes[0].Legs[0].Departure);

        Click(page, "Back to editor");
        page.Find("button[aria-label^='Edit connection 1:']").Click();
        var times = page.FindAll(".leg-panel input[type=datetime-local]");
        Assert.All(times, input => Assert.Equal("60", input.GetAttribute("step")));
        times[0].Change("2026-11-14T10:05:59.123");
        Click(page, "Review on map");
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
        Assert.Empty(JSInterop.Invocations[$"{unusedStorage}.getItem"]);
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
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
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

        Click(page, "Standard editor");
        Assert.EndsWith($"/new-trip/{TripId:D}", navigation.Uri);
        page.Find(".map-trip-name, .trip-basics input").Change("Updated weekend");
        Click(page, "Review on map");
        Assert.Contains("Updated weekend", page.Find("[aria-label='Trip review details']").TextContent);
        Assert.Equal(new[] { "01HotelAccommodation", "02MuseumAttraction" },
            page.FindAll(".trip-stop").Select(stop => string.Concat(stop.TextContent.Where(c => !char.IsWhiteSpace(c)))));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("entries are retained", page.Find("[role=alert]").TextContent));
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Updated weekend", page.Find("[role=status]").TextContent));
        Assert.EndsWith($"/new-trip/{TripId:D}", navigation.Uri);
        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Legs.Single(), writes[1].Legs.Single());
        Assert.Equal(Train.TransportModeId, writes[1].Legs.Single().TransportModeId);

        Click(page, "Back to editor");
        page.Find(".map-trip-name, .trip-basics input").Change("Edited again");
        Click(page, "Review on map");
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("Saved Edited again", page.Find("[role=status]").TextContent));
        Assert.Equal(3, writes.Count);

        navigation.NavigateTo("/new-trip");
        routes.WaitForAssertion(() =>
        {
            Assert.Equal("Trip editor", routes.Find("h1").TextContent);
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
        Click(page, "Review on map");
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
        var newPlace = Museum with { PlaceId = Guid.NewGuid() };
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
        Click(page, "Review on map");
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
            JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
            Click(page, "Restore map draft");
            Assert.Contains("Map draft restored from this browser.", page.Markup);
        }

        Click(page, "Review on map");
        Assert.Contains("They stay in the catalogue even if you discard this trip.", page.Find(".trip-review").TextContent);
        Assert.DoesNotContain(page.FindAll(".trip-review button"), button => button.TextContent == "Save trip");
        Assert.Equal(0, catalogueWrites);
        Click(page, "Save catalogue entries");
        page.WaitForAssertion(() => Assert.Equal(restoreExistingPlace ? 0 : 1, catalogueWrites));
        Assert.Equal(0, tripWrites);

        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("entries are retained", page.Find("[role=alert]").TextContent));
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find(".trip-message.success").TextContent));
        Assert.Equal(2, tripWrites);
        Assert.Equal(restoreExistingPlace ? 0 : 1, catalogueWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostPlaceResponseRetriesLookupWithoutCreatingAnotherRecord(bool committed)
    {
        var previous = new AttractionDto(Guid.NewGuid(), Museum.Name, null, null, "", 45.52m, -73.59m, 30);
        var created = previous with { AttractionId = Guid.NewGuid(), Name = " MUSEUM " };
        var nearMatch = created with { AttractionId = Guid.NewGuid(), Lon = -73.58m };
        var catalogueWrites = 0;
        var tripWrites = 0;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/attractions")
            {
                if (request.Method == HttpMethod.Get)
                {
                    Assert.Contains("name=Museum", request.RequestUri.Query);
                    AttractionDto[] results = committed && catalogueWrites > 0 ? [previous, nearMatch, created] : [previous];
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(results) };
                }

                Assert.Equal(HttpMethod.Post, request.Method);
                catalogueWrites++;
                throw new HttpRequestException("Response lost after the create request.");
            }

            Assert.Equal($"/api/v1/trips/{TripId:D}", path);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            tripWrites++;
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            Assert.Equal(created.AttractionId, Assert.Single(body.Legs).To.PlaceId);
            return Json(Saved);
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, previous.Lat + 0.000003m, previous.Lon - 0.000004m)));

        Click(page, "Review on map");
        Click(page, "Save catalogue entries");
        page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
        Click(page, "Save catalogue entries");

        if (committed)
        {
            Click(page, "Save trip");
            page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
            Assert.Equal(1, tripWrites);
        }
        else
        {
            page.WaitForAssertion(() => Assert.Contains("catalogue save for Museum could not be confirmed", page.Find("[role=alert]").TextContent));
            Assert.DoesNotContain(page.FindAll(".trip-review button"), button => button.TextContent == "Save trip");
            Assert.Equal(0, tripWrites);
        }

        Assert.Equal(1, catalogueWrites);
    }

    [Fact]
    public async Task CatalogueSaveRemainsSingleFlightAcrossTripRoutes()
    {
        var otherTrip = Saved with { TripId = Guid.NewGuid(), Name = "Other trip" };
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
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/map/{TripId:D}");
        var routes = Render<Routes>();
        var page = routes.FindComponent<NewTrip>();
        var canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
        await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.52m, -73.59m)));
        Click(page, "Review on map");
        var firstClick = page.Find(".trip-review button").TriggerEventAsync("onclick", new MouseEventArgs());

        try
        {
            await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await routes.InvokeAsync(() => navigation.NavigateTo($"/map/{otherTrip.TripId:D}"));
            page = routes.FindComponent<NewTrip>();
            canvas = page.FindComponent<TravelTracker.Frontend.Components.Trips.TripMapCanvas>();
            await page.InvokeAsync(() => canvas.Instance.StopMoved.InvokeAsync((1, 45.53m, -73.60m)));
            Click(page, "Review on map");
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
        Click(page, "Review on map");
        var save = page.Find(".trip-review button");
        var firstClick = save.TriggerEventAsync("onclick", new MouseEventArgs());

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(page.Find(".trip-review button").HasAttribute("disabled"));
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
        Assert.Equal("Trip editor", routes.Find("h1").TextContent);
        Assert.Equal("page", routes.Find("a[href='trip']").GetAttribute("aria-current"));
        Assert.Equal("Weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));

        Click(routes.FindComponent<NewTrip>(), "Map View");
        Assert.EndsWith("/map", navigation.Uri);
        Assert.Single(routes.FindAll(".trip-map-canvas"));

        Click(routes.FindComponent<NewTrip>(), "Standard editor");
        Assert.EndsWith("/trip", navigation.Uri);
        Assert.Empty(routes.FindAll(".trip-map-canvas"));
        Assert.Empty(JSInterop.Invocations["confirm"]);

        Task NavigateUsingHeader(string path) => routes.InvokeAsync(() =>
            navigation.NavigateTo(routes.Find($"a[href='{path}']").GetAttribute("href")!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingThePlannerWithUnsavedEditsRequiresConfirmation(bool discard)
    {
        Configure((_, _) => throw new InvalidOperationException("Leaving must not call a service."));
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(discard);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/new-trip");
        var routes = Render<Routes>();

        Assert.False(routes.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);
        routes.Find(".map-trip-name, .trip-basics input").Change("Unsaved weekend");
        Assert.True(routes.FindComponent<NavigationLock>().Instance.ConfirmExternalNavigation);

        await routes.InvokeAsync(() => navigation.NavigateTo("/"));

        Assert.Single(JSInterop.Invocations["confirm"]);
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
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/map");
        var routes = Render<Routes>();
        routes.Find(".map-trip-name, .trip-basics input").Change("Unsaved weekend");

        await routes.InvokeAsync(() => navigation.NavigateTo($"/map/{TripId:D}"));

        Assert.Single(JSInterop.Invocations["confirm"]);
        Assert.EndsWith("/map", navigation.Uri);
        Assert.Equal("Unsaved weekend", routes.Find(".map-trip-name, .trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public async Task MapPickedStopsAndLegStayInTheDraftWhenSwitchingToStandardEditor()
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not write to a catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();

        Click(page, "Markers");
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
        Assert.Contains("Standard editor", card.TextContent);
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
        Click(page, "Standard editor");

        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        Assert.Contains("Walk", page.Find(".trip-leg-time").TextContent);
        Assert.Empty(page.FindAll(".trip-marker"));
        Click(page, "Review on map");
        Assert.Equal(2, page.FindAll(".trip-marker").Count);
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

        Assert.Equal(new[] { "Accommodation 1", "Stop 3", "Attraction 2", "Accommodation 1" },
            page.FindAll(".trip-stop strong").Select(name => name.TextContent));
        Assert.Equal(new[] { "Train", "Bus", "Walk" }, page.FindAll(".map-leg-label span").Select(label => label.TextContent));
        Assert.Equal(3, page.FindAll(".route-line").Count);

        Click(page, "Review on map");
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
        var walk = new TripDto.Mode(Guid.NewGuid(), "pedestrian", "Walk");
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
                Assert.Contains("code=pedestrian", request.RequestUri.Query);
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
            Click(page, "+ Return to starting accommodation");
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

        Click(page, "Review on map");
        Assert.Empty(page.FindAll("[role=alert]"));
        Click(page, "Save catalogue entries");
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
        Click(page, "+ Return to starting accommodation");
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
    [InlineData(false)]
    [InlineData(true)]
    public void MapPlannerCreatesATripFromSavedPlacesAndReusesTheReturnedId(bool loseModeResponse)
    {
        SaveTripRequest? created = null;
        var modeWrites = 0;
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
                        Assert.Contains("code=train", request.RequestUri.Query);
                        var modes = loseModeResponse && modeWrites == 0 ? Array.Empty<TripDto.Mode>() : [Train];
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(modes) };
                    }

                    Assert.True(loseModeResponse);
                    Assert.Equal(HttpMethod.Post, request.Method);
                    modeWrites++;
                    throw new HttpRequestException("Response lost after creating the transport mode.");
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
        page.FindAll(".trip-basics input[type=datetime-local]")[0].Change("2026-11-14T09:00");
        page.FindAll(".trip-basics input[type=datetime-local]")[1].Change("2026-11-14T13:00:00");
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
        Click(page, "Review on map");
        Assert.True(page.FindAll("[role=alert]").Count == 0, string.Join(" ", page.FindAll("[role=alert]").Select(alert => alert.TextContent)));
        Click(page, "Save catalogue entries");
        if (loseModeResponse)
        {
            page.WaitForAssertion(() => Assert.Contains("could not be confirmed", page.Find("[role=alert]").TextContent));
            Click(page, "Save catalogue entries");
        }
        Click(page, "Save map draft");
        Click(page, "Save trip");

        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[role=status]"), item => item.TextContent.Contains("Saved Weekend")));
        Assert.Collection(JSInterop.Invocations["localStorage.setItem"],
            item => Assert.EndsWith("-new", Assert.IsType<string>(item.Arguments[0])),
            item => Assert.EndsWith($"-{TripId:D}", Assert.IsType<string>(item.Arguments[0])));
        Assert.Equal(loseModeResponse ? 1 : 0, modeWrites);
        Assert.NotNull(created);
        Assert.Equal(Start, created.Start);
        var leg = Assert.Single(created.Legs);
        Assert.Equal(Hotel.PlaceId, leg.From.PlaceId);
        Assert.Equal(Museum.PlaceId, leg.To.PlaceId);
        Assert.Equal(Train.TransportModeId, leg.TransportModeId);
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

        page.FindAll(".trip-basics input[type=datetime-local]")[0].Change("2026-11-01T01:00");
        page.FindAll(".trip-basics input[type=datetime-local]")[1].Change("2026-11-01T01:45");
        page.FindAll(".trip-basics select")[0].Change("-04:00:00");
        page.FindAll(".trip-basics select")[1].Change("-05:00:00");

        page.Find("button[aria-label^='Edit connection 1:']").Click();
        page.FindAll(".leg-panel input[type=datetime-local]")[0].Change("2026-11-01T01:30");
        page.FindAll(".leg-panel input[type=datetime-local]")[1].Change("2026-11-01T01:30");
        page.FindAll(".leg-panel select")[0].Change("-04:00:00");
        page.FindAll(".leg-panel select")[1].Change("-05:00:00");

        Assert.Equal("1", page.Find("input[aria-label='Duration hours']").GetAttribute("value"));
        Click(page, "Review on map");
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
        var station = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "Station", "", 45.52m, -73.59m);
        var overnight = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Overnight hotel", "", 45.53m, -73.60m);
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
        var station = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "Station", "", Hotel.Lat, Hotel.Lon);
        var walk = new TripDto.Mode(Guid.NewGuid(), "pedestrian", "Walk");
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

        Click(page, "Review on map");
        Assert.Equal(2, page.FindAll(".review-legs li").Count);
        Assert.Empty(page.FindAll("[role=alert]"));
    }

    [Fact]
    public async Task MapPageServesItsFigmaIconsAndUndoShortcut()
    {
        await using var factory = new FrontendFactory(signInConfigured: false);
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
        bool withAccommodationCatalogue = false)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clock = new FakeTimeProvider(Start);
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", Start.AddHours(1),
            withAccommodationCatalogue ? "catalogue.read" : "users.read users.write");
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var directory = new ServiceDirectory(new Dictionary<string, string>
        {
            ["Trips"] = "http://localhost:5108",
            ["Accommodations"] = "http://localhost:5106",
            ["Attractions"] = "http://localhost:5104",
            ["Transport"] = "http://localhost:5105"
        });
        var services = new ServiceClient(new HttpClient(new StubHandler(respond)), NullLogger<ServiceClient>.Instance);
        var client = new ProtectedClient(authentication, directory, services, clock, NullLogger<ProtectedClient>.Instance, sessions);
        var transport = new TransportClient(client);
        Services.AddSingleton(transport);
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new TripPlacesClient(new AttractionsClient(client), transport, client));
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

    static void Click(IRenderedComponent<NewTrip> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == label).Click();
}
