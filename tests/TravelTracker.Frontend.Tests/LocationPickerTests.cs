using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Components.Accommodations;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Features.Accommodations;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Proves address-first location selection never invents coordinates or writes unconfirmed form changes. */
public sealed class LocationPickerTests : BunitContext
{
    public LocationPickerTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    void ConfigureRouting(StubHandler handler)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-23T12:00:00Z"));
        var authentication = new SwitchableAuthentication(new(clock));
        authentication.SignIn("routing-session", clock.GetUtcNow().AddMinutes(10));
        Services.AddSingleton(new RoutingClient(new ProtectedClient(authentication,
            new(new Dictionary<string, string> { ["Routing"] = "https://routing.example" }),
            new(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
            NullLogger<ProtectedClient>.Instance, authentication.Sessions)));
    }

    [Fact]
    public void UnknownManualAddressDoesNotUseTheInitialMapCenterAsItsPosition()
    {
        (string, string)? used = null;
        var picker = Render<LocationPicker>(parameters => parameters.Add(component => component.Address, "An unknown place")
            .Add(component => component.UseLocation, value => used = value));
        Assert.Empty(picker.FindComponent<TripMapCanvas>().Instance.Stops);
        Assert.Contains("Map position not selected", picker.Find(".location-coordinate-readout").TextContent);
        Assert.Equal("An unknown place", picker.Find(".address-search input").GetAttribute("value"));

        picker.Find(".location-actions button:not(.secondary)").Click();

        Assert.Null(used);
        Assert.Contains("Choose an address result or a point", picker.Find("[role=alert]").TextContent);
        Assert.Empty(picker.FindAll("input[required]"));
    }

    [Fact]
    public async Task MapChoiceAndManualAddressRemainLocalUntilUse()
    {
        (string Lat, string Lon)? used = null;
        string? address = null;
        var picker = Render<LocationPicker>(parameters => parameters
            .Add(component => component.UseLocation, value => used = value)
            .Add(component => component.AddressChanged, value => address = value));
        await picker.InvokeAsync(() => picker.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((45.51m, -73.59m)));
        picker.Find(".address-search input").Input("My chosen map address");
        Assert.Null(used);
        Assert.Null(address);

        picker.Find(".location-actions button:not(.secondary)").Click();

        Assert.Equal(("45.51", "-73.59"), used);
        Assert.Equal("My chosen map address", address);
    }

    [Fact]
    public async Task CancellingAnEditedSavedLocationPreservesAllIncomingValues()
    {
        var cancelled = false;
        var changed = false;
        var picker = Render<LocationPicker>(parameters => parameters.Add(component => component.Address, "Saved address")
            .Add(component => component.Latitude, "45.5").Add(component => component.Longitude, "-73.6")
            .Add(component => component.AddressChanged, _ => changed = true)
            .Add(component => component.UseLocation, _ => changed = true)
            .Add(component => component.Cancel, () => cancelled = true));
        await picker.InvokeAsync(() => picker.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((46m, -74m)));
        picker.Find(".address-search input").Input("Unconfirmed new address");
        picker.Find(".location-actions button.secondary").Click();
        Assert.True(cancelled);
        Assert.False(changed);
        Assert.Equal("Saved address", picker.Instance.Address);
        Assert.Equal("45.5", picker.Instance.Latitude);
        Assert.Equal("-73.6", picker.Instance.Longitude);
    }

    [Theory]
    [InlineData("attraction")]
    [InlineData("accommodation")]
    [InlineData("transport_stop")]
    public async Task AddressSearchMovesAnExistingLocationOnlyAfterCandidateAndLocationConfirmation(string placeType)
    {
        ConfigureRouting(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AddressLookupResult("success",
                [new("Selected address", 45.51m, -73.59m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
        })));
        (string Lat, string Lon)? used = null;
        var picker = Render<LocationPicker>(parameters => parameters.Add(component => component.UseLocation, value => used = value)
            .Add(component => component.PlaceType, placeType).Add(component => component.Latitude, "45.5")
            .Add(component => component.Longitude, "-73.6").Add(component => component.Address, "Old address"));
        picker.Find(".address-search input").Input("Selected address");
        await picker.Find(".address-search").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Equal((45.5m, -73.6m), (picker.FindComponent<TripMapCanvas>().Instance.Stops[0].Lat,
            picker.FindComponent<TripMapCanvas>().Instance.Stops[0].Lon));
        picker.Find(".address-candidates button").Click();
        Assert.Single(picker.FindComponent<TripMapCanvas>().Instance.Stops);
        Assert.Null(used);
        picker.Find(".location-actions button:not(.secondary)").Click();
        Assert.Equal(("45.51", "-73.59"), used);
    }

    [Fact]
    public async Task FailedAddressSearchKeepsTheSavedPositionAndEditableAddress()
    {
        ConfigureRouting(StubHandler.Status(503));
        var picker = Render<LocationPicker>(parameters => parameters.Add(component => component.Address, "Saved address")
            .Add(component => component.Latitude, "45.5").Add(component => component.Longitude, "-73.6"));
        picker.Find(".address-search input").Input("Unresolved address");
        await picker.Find(".address-search").TriggerEventAsync("onsubmit", EventArgs.Empty);
        var stop = Assert.Single(picker.FindComponent<TripMapCanvas>().Instance.Stops);
        Assert.Equal((45.5m, -73.6m), (stop.Lat, stop.Lon));
        Assert.Equal("Unresolved address", picker.Find(".address-search input").GetAttribute("value"));
        Assert.Contains("unavailable", picker.Find("[role=status]").TextContent);
    }

    [Fact]
    public async Task FailedReverseLookupKeepsTheManuallyEnteredAddressAfterAMapChoice()
    {
        ConfigureRouting(StubHandler.Status(503));
        var picker = Render<LocationPicker>(parameters => parameters.Add(component => component.Address, "My unmapped cabin"));
        await picker.InvokeAsync(() => picker.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((45.51m, -73.59m)));
        picker.WaitForAssertion(() => Assert.Contains("unavailable", picker.Find("[role=status]").TextContent));
        Assert.Equal("My unmapped cabin", picker.Find(".address-search input").GetAttribute("value"));
        var stop = Assert.Single(picker.FindComponent<TripMapCanvas>().Instance.Stops);
        Assert.Equal((45.51m, -73.59m), (stop.Lat, stop.Lon));
    }

    [Fact]
    public async Task EmbeddedMapPickerHasNoNestedFormAndDoesNotRequireNumericEntry()
    {
        var draft = new AccommodationDraft { Name = "Map hotel", Address = "Unknown" };
        var fields = Render<AccommodationFormFields>(parameters => parameters.Add(component => component.Draft, draft)
            .Add(component => component.Prefix, "map-hotel"));
        fields.FindAll("button").Single(button => button.TextContent == "Choose location on map").Click();
        Assert.Empty(fields.FindAll("form"));
        Assert.Single(fields.FindAll(".address-search textarea"));
        Assert.Empty(fields.FindAll("input[inputmode=decimal][required]"));
        Assert.False(fields.Find("#map-hotel-lat").Closest("details")!.HasAttribute("open"));
        await fields.InvokeAsync(() => fields.FindComponent<TripMapCanvas>().Instance.LocationPicked.InvokeAsync((45.5m, -73.6m)));
        fields.Find(".address-search textarea").Input("Map hotel address");
        Assert.Equal("", draft.Lat);
        fields.Find(".location-actions button:not(.secondary)").Click();
        Assert.Equal(("45.5", "-73.6", "Map hotel address"), (draft.Lat, draft.Lon, draft.Address));
        Assert.Empty(draft.Validate());
    }

    [Fact]
    public void EmbeddedAddressSearchUsesAButtonAndNeverSubmitsTheParentPlaceForm()
    {
        ConfigureRouting(new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AddressLookupResult("success",
                [new("Chosen embedded address", 45.51m, -73.59m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
        })));
        var host = Render<EmbeddedLocationForm>();
        Assert.Single(host.FindAll("form"));
        host.Find(".address-search textarea").Input("Chosen embedded address");
        var search = host.Find(".address-actions button");
        Assert.Equal("button", search.GetAttribute("type"));
        search.Click();
        host.WaitForAssertion(() => Assert.Single(host.FindAll(".address-candidates button")));
        host.Find(".address-candidates button").Click();
        Assert.Equal(0, host.Instance.Submissions);
        Assert.Null(host.Instance.Used);
        host.Find(".location-actions button:not(.secondary)").Click();
        Assert.Equal(("45.51", "-73.59"), host.Instance.Used);
        Assert.Equal(0, host.Instance.Submissions);
    }

    public sealed class EmbeddedLocationForm : ComponentBase
    {
        public int Submissions { get; private set; }
        public (string Lat, string Lon)? Used { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "form");
            builder.AddAttribute(1, "onsubmit", EventCallback.Factory.Create(this, () => Submissions++));
            builder.OpenComponent<LocationPicker>(2);
            builder.AddAttribute(3, nameof(LocationPicker.Embedded), true);
            builder.AddAttribute(4, nameof(LocationPicker.UseLocation),
                EventCallback.Factory.Create<(string Lat, string Lon)>(this, value => Used = value));
            builder.CloseComponent();
            builder.CloseElement();
        }
    }
}
