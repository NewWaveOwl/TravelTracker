using Bunit;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Tests;

/** exercises map picks and marker selections through rendered Blazor controls. */
public sealed class TripMapCanvasTests : BunitContext
{
    public TripMapCanvasTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public async Task OpeningAnotherTripFitsItsStopsAfterPanningThePreviousTrip()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var montreal = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Montreal", "", 45.5m, -73.57m);
        var paris = montreal with { PlaceId = Guid.NewGuid(), Name = "Paris", Lat = 48.86m, Lon = 2.35m };
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.TripId, firstId)
            .Add(component => component.Stops, [montreal]));
        await map.InvokeAsync(() => map.Instance.PanByDrag(4000, 0));
        Assert.Empty(map.FindAll(".trip-marker"));

        map.Render(parameters => parameters
            .Add(component => component.TripId, secondId)
            .Add(component => component.Stops, [paris]));

        Assert.Single(map.FindAll(".trip-marker[aria-label='Stop 1: Paris']"));
        await map.InvokeAsync(() => map.Instance.PanByDrag(4000, 0));
        map.Render(parameters => parameters.Add(component => component.Stops, [paris with { Name = "Edited Paris" }]));
        Assert.Empty(map.FindAll(".trip-marker"));
    }

    [Fact]
    public async Task SelectingAndEditingOffscreenMarkersPreservesTheCameraUntilRecentering()
    {
        var hotel = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Hotel", "", 45.5019m, -73.5674m);
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        var viewport = module.SetupModule("watchViewport", _ => true);
        viewport.SetupVoid("setCamera", _ => true).SetVoidResult();
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [hotel]));
        await map.InvokeAsync(() => map.Instance.PanByDrag(4000, 0));
        Assert.Empty(map.FindAll(".trip-marker"));
        var changes = viewport.Invocations["setCamera"].Count;

        map.Render(parameters => parameters.Add(component => component.SelectedIndex, 0));
        Assert.Equal(changes, viewport.Invocations["setCamera"].Count);
        map.Render(parameters => parameters.Add(component => component.Stops, [hotel with { Name = "Edited hotel", Lon = -74m }]));
        Assert.Equal(changes, viewport.Invocations["setCamera"].Count);
        Assert.Empty(map.FindAll(".trip-marker"));

        map.Find("button[aria-label='Recenter map']").Click();
        Assert.Single(map.FindAll(".trip-marker[aria-label='Stop 1: Edited hotel']"));
        Assert.Equal(changes + 1, viewport.Invocations["setCamera"].Count);
    }

    [Theory]
    [InlineData(false, 43.6)]
    [InlineData(true, 43.6)]
    [InlineData(true, 70)]
    public async Task RecenterUsesStartingStopOrInitialCenterAndPreservesZoomWithoutPicking(bool withStops, double latitude)
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        var viewport = module.SetupModule("watchViewport", _ => true);
        viewport.SetupVoid("setCamera", _ => true).SetVoidResult();
        var start = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Start", "", (decimal)latitude, -79.3m);
        var destination = start with { PlaceId = Guid.NewGuid(), Name = "Destination", Lat = 48.8m, Lon = 2.3m };
        var picks = 0;
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, withStops ? [start, destination] : [])
            .Add(component => component.SelectedIndex, 1).Add(component => component.CanPick, true)
            .Add(component => component.LocationPicked, _ => picks++));
        map.Find("[aria-label='Zoom in']").Click();
        await map.InvokeAsync(() => map.Instance.PanByDrag(500, 100));
        var previous = Assert.IsType<double[]>(viewport.Invocations["setCamera"].Last().Arguments[0]);
        map.Find("[aria-label='Recenter map']").Click();
        var camera = Assert.IsType<double[]>(viewport.Invocations["setCamera"].Last().Arguments[0]);
        Assert.Equal(withStops ? -79.3 : -73.5674, camera[0], 6);
        Assert.Equal(withStops ? latitude : 45.5019, camera[1], 6);
        Assert.Equal(previous[2], camera[2]);
        Assert.Equal(0, picks);
    }

    [Fact]
    public void SharedPinCyclesBetweenReturnVisits()
    {
        var hotel = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009001"), "accommodation", "Hotel", "", 45.5019m, -73.5674m);
        var museum = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009002"), "attraction", "Museum", "", 45.4988m, -73.5796m);
        var selected = -1;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.Stops, [hotel, museum, hotel])
            .Add(component => component.SelectedIndex, 0)
            .Add(component => component.StopSelected, index => selected = index));

        Assert.Equal(2, map.FindAll(".trip-marker").Count);
        var start = map.Find("[aria-label='Stop 1: Hotel']");
        var position = start.GetAttribute("transform");
        Assert.Equal("true", start.GetAttribute("aria-pressed"));
        Assert.Contains("select stop 3", start.GetAttribute("aria-description"));
        start.Click();
        Assert.Equal(2, selected);

        map.Render(parameters => parameters.Add(component => component.SelectedIndex, selected));
        var end = map.Find("[aria-label='Stop 3: Hotel']");
        Assert.Equal(position, end.GetAttribute("transform"));
        Assert.Equal("true", end.GetAttribute("aria-pressed"));
        Assert.Equal("03+", end.QuerySelector("text")!.TextContent);
        Assert.Equal(2, map.FindAll(".trip-marker").Count);

        end.KeyDown("Enter");
        Assert.Equal(0, selected);
        Assert.Empty(map.FindAll(".route-line, .route-halo"));
    }

    [Fact]
    public void FittingStopsAcrossTheDateLineKeepsBothMarkersVisible()
    {
        var east = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009003"), "transport_stop", "East", "", 0, 179.9m);
        var west = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009004"), "transport_stop", "West", "", 0, -179.9m);
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [east, west]));

        Assert.Equal(2, map.FindAll(".trip-marker").Count);
        Assert.Single(map.FindAll(".map-basemap"));
        Assert.Empty(map.FindAll("image[href^='https://tile.openstreetmap.org/']"));
        var pins = map.FindAll(".trip-marker image");
        Assert.Equal(2, pins.Count);
        Assert.All(pins, pin =>
        {
            Assert.Equal("assets/trip-map/pin-transport_stop.svg", pin.GetAttribute("href"));
            Assert.Equal("54", pin.GetAttribute("width"));
            Assert.Equal("76", pin.GetAttribute("height"));
        });
    }

    [Fact]
    public void CenterPickProvidesGeographicCoordinatesAndMovesWhenPanning()
    {
        (decimal Lat, decimal Lon) selected = default;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.CanPick, true)
            .Add(component => component.FullScreen, true)
            .Add(component => component.LocationPicked, point => selected = point));

        var controls = map.FindAll(".map-controls button");
        Assert.Equal(4, controls.Count);
        Assert.Equal(new[] { "zoom-in.svg", "zoom-out.svg", "map-center.svg", "pin-attraction.svg" },
            map.FindAll(".map-controls img").Select(image => Path.GetFileName(image.GetAttribute("src"))));
        var center = map.Find(".map-controls button[aria-label='Pick map center']");
        center.Click();
        var initial = selected;
        Assert.Equal((45.5019m, -73.5674m), initial);
        map.Find("svg").KeyDown("ArrowRight");
        center.Click();
        Assert.Equal(initial.Lat, selected.Lat);
        Assert.True(selected.Lon > initial.Lon);
        map.Render(parameters => parameters.Add(component => component.CanPick, false));
        Assert.Empty(map.FindAll("[aria-label='Pick map center']"));
        Assert.Single(map.FindAll("[aria-label='Recenter map']"));
    }

    [Fact]
    public async Task FullMapUsesTheMeasuredViewportAndKeepsPinsAtTheirExportedSize()
    {
        var hotel = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009005"), "accommodation", "Hotel", "", 45.5019m, -73.5674m);
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.FullScreen, true)
            .Add(component => component.Stops, [hotel]));

        await map.InvokeAsync(() => map.Instance.ResizeViewport(1600, 800));

        Assert.Equal("0 0 1600 800", map.Find("svg").GetAttribute("viewBox"));
        var pin = map.Find(".trip-marker image");
        Assert.Equal("54", pin.GetAttribute("width"));
        Assert.Equal("76", pin.GetAttribute("height"));
        Assert.Equal("assets/trip-map/pin-accommodation.svg", pin.GetAttribute("href"));
    }

    [Fact]
    public async Task BasemapFollowsPlannerZoomAndCanRetryAfterLoadFailure()
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        var viewport = module.SetupModule("watchViewport", _ => true);
        viewport.SetupVoid("setCamera", _ => true).SetVoidResult();
        viewport.SetupVoid("retryBasemap", _ => true).SetVoidResult();
        var map = Render<TripMapCanvas>();

        var initialCamera = Assert.IsType<double[]>(module.VerifyInvoke("watchViewport").Arguments[3]);
        Assert.Equal(-73.5674, initialCamera[0], 6);
        Assert.Equal(45.5019, initialCamera[1], 6);
        Assert.Equal(12, initialCamera[2]);

        map.Find("button[aria-label='Zoom in']").Click();
        var zoomedCamera = Assert.IsType<double[]>(viewport.VerifyInvoke("setCamera").Arguments[0]);
        Assert.Equal(initialCamera[0], zoomedCamera[0], 6);
        Assert.Equal(initialCamera[1], zoomedCamera[1], 6);
        Assert.Equal(13, zoomedCamera[2]);

        await map.InvokeAsync(() => map.Instance.SetBasemapReady(false));
        Assert.Equal("Map background unavailable.", map.Find(".map-status [aria-live]").TextContent);
        map.Find(".map-status button").Click();
        viewport.VerifyInvoke("retryBasemap");

        await map.InvokeAsync(() => map.Instance.SetBasemapReady(true));
        Assert.Empty(map.FindAll(".map-status"));
    }

    [Fact]
    public async Task WheelZoomKeepsThePlaceUnderThePointerAndUpdatesTheBasemap()
    {
        const double x = 576;
        const double y = 108;
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        var viewport = module.SetupModule("watchViewport", _ => true);
        viewport.SetupVoid("setCamera", _ => true).SetVoidResult();
        module.Setup<double[]>("viewBoxPoint", _ => true).SetResult([x, y]);
        (decimal Lat, decimal Lon) selected = default;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.CanPick, true)
            .Add(component => component.LocationPicked, point => selected = point));

        map.Find(".location-target").Click();
        var original = selected;

        await map.InvokeAsync(() => map.Instance.ZoomAtPointer(1, x, y));
        map.Find(".location-target").Click();
        Assert.Equal(original, selected);
        Assert.Equal(13, Assert.IsType<double[]>(viewport.VerifyInvoke("setCamera").Arguments[0])[2]);

        await map.InvokeAsync(() => map.Instance.ZoomAtPointer(-1, x, y));
        map.Find(".location-target").Click();
        Assert.Equal(original, selected);
        Assert.Equal(12, Assert.IsType<double[]>(viewport.VerifyInvoke("setCamera", 2)[1].Arguments[0])[2]);
    }

    [Fact]
    public async Task NearbyConnectionsUseBadgesOnTheirOwnCurvesAndCanStillBeEdited()
    {
        var west = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009006"), "transport_stop", "West", "", 0, -.15m);
        var east = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009007"), "transport_stop", "East", "", 0, .15m);
        (int Destination, bool FromKeyboard) selected = default;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.Stops, [west, east, west])
            .Add(component => component.Legs, [("train", "Train · 30 min"), ("bus", "Bus · 40 min")])
            .Add(component => component.ConnectionSelected, activation => selected = activation));

        Assert.Equal(2, map.FindAll(".map-leg-label span:not([hidden])").Count);
        Assert.Empty(map.FindAll(".map-leg-badge"));

        await map.InvokeAsync(() => map.Instance.ZoomAtPointer(-1, 384, 216));
        Assert.Empty(map.FindAll(".map-leg-badge"));
        await map.InvokeAsync(() => map.Instance.ZoomAtPointer(-1, 384, 216));

        var badges = map.FindAll(".map-leg-badge");
        Assert.Equal(2, badges.Count);
        Assert.All(badges, badge =>
        {
            Assert.Equal("369.5", badge.GetAttribute("x"));
            Assert.Equal("29", badge.GetAttribute("width"));
            Assert.Equal("29", badge.GetAttribute("height"));
        });
        Assert.Equal("183.5", badges[0].GetAttribute("y"));
        Assert.Equal("219.5", badges[1].GetAttribute("y"));
        Assert.Empty(map.FindAll(".map-leg-label span:not([hidden])"));
        Assert.Equal("assets/trip-map/train.svg", badges[0].QuerySelector("img")!.GetAttribute("src"));
        var train = map.Find(".map-leg-badge button[aria-label$='Train · 30 min']");
        train.Click(new MouseEventArgs { Detail = 1 });
        Assert.Equal((1, false), selected);
        train.Click(new MouseEventArgs { Detail = 0 });
        Assert.Equal((1, true), selected);

        map.Find("button[aria-label='Zoom in']").Click();

        Assert.Empty(map.FindAll(".map-leg-badge"));
        Assert.Equal(2, map.FindAll(".map-leg-label span:not([hidden])").Count);
    }

    [Fact]
    public void LongFlightsKeepTheirDurationWhileNearbyConnectionsShrinkAtTheSameZoom()
    {
        var toronto = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009008"), "transport_stop", "Toronto", "", 43.68m, -79.63m);
        var montreal = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009009"), "transport_stop", "Montreal", "", 45.47m, -73.74m);
        var paris = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009010"), "transport_stop", "Paris", "", 49.01m, 2.55m);
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.Stops, [toronto, montreal, paris])
            .Add(component => component.Legs, [("train", "Train · 5 h"), ("plane", "Plane · 7 h")]));

        Assert.Equal("Plane · 7 h", map.Find(".map-leg-label span:not([hidden])").TextContent);
        Assert.Equal("26", map.Find(".map-leg-badge").GetAttribute("width"));

        map.Find("button[aria-label='Zoom out']").Click();

        Assert.Equal("Plane · 7 h", map.Find(".map-leg-label span:not([hidden])").TextContent);
        Assert.Equal("25", map.Find(".map-leg-badge").GetAttribute("width"));
    }

    [Fact]
    public void ZoomedOutStopsUseCompactTypeIconsAndRemainSelectable()
    {
        var hotel = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009011"), "accommodation", "Hotel", "", 0, -.15m);
        var museum = new TripPlaceDto(Guid.Parse("00000000-0000-0000-0000-000000009012"), "attraction", "Museum", "", 0, .15m);
        var selected = -1;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.Stops, [hotel, museum])
            .Add(component => component.StopSelected, index => selected = index));

        map.Find("button[aria-label='Zoom out']").Click();

        Assert.Equal(2, map.FindAll(".marker-badge").Count);
        var badge = map.Find("[aria-label='Stop 2: Museum']");
        Assert.Equal("assets/trip-map/attraction.svg", badge.QuerySelector("image")!.GetAttribute("href"));
        badge.Click();
        Assert.Equal(1, selected);

        map.Find("button[aria-label='Zoom in']").Click();

        Assert.Empty(map.FindAll(".marker-badge"));
        Assert.Equal("assets/trip-map/pin-attraction.svg",
            map.Find("[aria-label='Stop 2: Museum'] image").GetAttribute("href"));
    }

    [Fact]
    public void PointerPickUsesTheConvertedSvgPosition()
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        module.Setup<double[]>("viewBoxPoint", _ => true).SetResult([576, 108]);
        (decimal Lat, decimal Lon) selected = default;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.CanPick, true)
            .Add(component => component.LocationPicked, point => selected = point));

        map.Find(".location-target").Click(new MouseEventArgs { ClientX = 173, ClientY = 82 });

        Assert.True(selected.Lat > 45.5019m);
        Assert.True(selected.Lon > -73.5674m);
        var call = module.VerifyInvoke("viewBoxPoint");
        Assert.Equal(173d, call.Arguments[1]);
        Assert.Equal(82d, call.Arguments[2]);
    }

    [Theory]
    [InlineData(true, 16, 12, false, "standard")]
    [InlineData(true, 6, 8, true, "low")]
    [InlineData(false, 16, 12, false, "low")]
    public void AutomaticQualityFollowsTheDeviceWhenTheBasemapStarts(bool webGl, double memory, int cores, bool coarse, string expected)
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        module.Setup<MapCapabilities>("mapCapabilities", _ => true)
            .SetResult(new MapCapabilities(webGl, memory, cores, coarse));
        module.SetupModule("watchViewport", _ => true);

        Render<TripMapCanvas>();

        Assert.Equal(expected, module.VerifyInvoke("watchViewport").Arguments[4]);
    }

    [Fact]
    public void UnreportedCapabilitiesUseLowQuality()
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        module.SetupModule("watchViewport", _ => true);

        Render<TripMapCanvas>();

        Assert.Equal("low", module.VerifyInvoke("watchViewport").Arguments[4]);
    }

    [Fact]
    public void ChangingQualityRebuildsTheBasemapWithoutMovingTheCamera()
    {
        var module = JSInterop.SetupModule("./Components/Trips/TripMapCanvas.razor.js");
        module.Setup<MapCapabilities>("mapCapabilities", _ => true)
            .SetResult(new MapCapabilities(WebGl: true, DeviceMemoryGigabytes: 16, LogicalCores: 12, CoarsePointer: false));
        var viewport = module.SetupModule("watchViewport", _ => true);
        viewport.SetupVoid("setCamera", _ => true).SetVoidResult();
        viewport.SetupVoid("setQuality", _ => true).SetVoidResult();
        var map = Render<TripMapCanvas>();
        var cameraChanges = viewport.Invocations["setCamera"].Count;

        map.Render(parameters => parameters.Add(component => component.QualityPreference, MapQuality.Low));

        Assert.Equal("low", Assert.Single(viewport.Invocations["setQuality"]).Arguments[0]);
        Assert.Equal(cameraChanges, viewport.Invocations["setCamera"].Count);
        map.Render(parameters => parameters.Add(component => component.QualityPreference, MapQuality.Low));
        Assert.Single(viewport.Invocations["setQuality"]);
    }
}
