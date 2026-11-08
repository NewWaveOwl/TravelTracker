using Bunit;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Trips;

namespace TravelTracker.Frontend.Tests;

/** Exercises map picks and marker selections through rendered Blazor controls. */
public sealed class TripMapCanvasTests : BunitContext
{
    [Fact]
    public void ReturnStopsAtTheSameCoordinatesStayIndividuallySelectable()
    {
        var hotel = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Hotel", "", 45.5019m, -73.5674m);
        var museum = new TripPlaceDto(Guid.NewGuid(), "attraction", "Museum", "", 45.4988m, -73.5796m);
        var selected = -1;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.Stops, [hotel, museum, hotel])
            .Add(component => component.SelectedIndex, 0)
            .Add(component => component.StopSelected, index => selected = index));

        var start = map.Find("[aria-label='Stop 1: Hotel']");
        var end = map.Find("[aria-label='Stop 3: Hotel']");
        Assert.NotEqual(start.GetAttribute("transform"), end.GetAttribute("transform"));
        Assert.Equal("true", start.GetAttribute("aria-pressed"));
        Assert.Equal("false", end.GetAttribute("aria-pressed"));
        end.Click();
        Assert.Equal(2, selected);
        Assert.Empty(map.FindAll(".route-line, .route-halo"));
    }

    [Fact]
    public void FittingStopsAcrossTheDateLineKeepsBothMarkersVisible()
    {
        var east = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "East", "", 0, 179.9m);
        var west = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "West", "", 0, -179.9m);
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [east, west]));

        Assert.Equal(2, map.FindAll(".trip-marker").Count);
        Assert.NotEmpty(map.FindAll("image[href^='https://tile.openstreetmap.org/']"));
        Assert.All(map.FindAll(".trip-marker image"), pin =>
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
            .Add(component => component.LocationPicked, point => selected = point));

        var controls = map.FindAll(".map-controls button");
        Assert.Equal(3, controls.Count);
        Assert.Equal(new[] { "zoom-in.svg", "zoom-out.svg", "map-center.svg" },
            map.FindAll(".map-controls img").Select(image => Path.GetFileName(image.GetAttribute("src"))));
        var center = map.Find("button[aria-label='Pick map center']");
        center.Click();
        var initial = selected;
        Assert.Equal((45.5019m, -73.5674m), initial);
        map.Find("svg").KeyDown("ArrowRight");
        center.Click();
        Assert.Equal(initial.Lat, selected.Lat);
        Assert.True(selected.Lon > initial.Lon);
    }

    [Fact]
    public async Task FullMapUsesTheMeasuredViewportAndKeepsPinsAtTheirExportedSize()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var hotel = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Hotel", "", 45.5019m, -73.5674m);
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
}
