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
        Assert.Single(map.FindAll(".route-line"));
    }

    [Fact]
    public void FittingStopsAcrossTheDateLineKeepsBothMarkersVisible()
    {
        var east = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "East", "", 0, 179.9m);
        var west = new TripPlaceDto(Guid.NewGuid(), "transport_stop", "West", "", 0, -179.9m);
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [east, west]));

        Assert.Equal(2, map.FindAll(".trip-marker").Count);
        Assert.All(map.FindAll("image"), tile => Assert.StartsWith("https://tile.openstreetmap.org/", tile.GetAttribute("href")));
    }

    [Fact]
    public void CenterPickProvidesGeographicCoordinatesAndMovesWhenPanning()
    {
        (decimal Lat, decimal Lon) selected = default;
        var map = Render<TripMapCanvas>(parameters => parameters
            .Add(component => component.CanPick, true)
            .Add(component => component.LocationPicked, point => selected = point));

        var center = map.FindAll("button").Single(button => button.TextContent == "Pick map center");
        center.Click();
        var initial = selected;
        Assert.Equal((45.5019m, -73.5674m), initial);
        map.Find("[aria-label='Pan east']").Click();
        center.Click();
        Assert.Equal(initial.Lat, selected.Lat);
        Assert.True(selected.Lon > initial.Lon);
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
