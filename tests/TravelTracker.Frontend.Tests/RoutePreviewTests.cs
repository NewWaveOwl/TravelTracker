using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Features.Trips;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Proves chosen routes remain previews, and real geometry stays distinct from approximate connections. */
public sealed class RoutePreviewTests : BunitContext
{
    static readonly DateTimeOffset Departure = DateTimeOffset.Parse("2026-11-23T10:00:00-05:00");
    static readonly RoutePlanRequest Request = new(new(45.5m, -73.6m), new(45.6m, -73.7m), "bus", Departure);
    static readonly RouteItinerary Candidate = new("route-1", 600, 1200, "scheduled",
        [new("BUS", 600, 1200, Departure, Departure.AddMinutes(10), "_vutG~~e\u0060M_pR~oR", "Bus 10", false)]);

    RoutingClient Client(StubHandler handler)
    {
        var clock = new FakeTimeProvider(Departure.AddHours(-1));
        var authentication = new SwitchableAuthentication(new(clock));
        authentication.SignIn("routing-session", clock.GetUtcNow().AddMinutes(10));
        return new(new ProtectedClient(authentication, new(new Dictionary<string, string> { ["Routing"] = "https://routing.example" }),
            new(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, authentication.Sessions));
    }

    static HttpResponseMessage Success() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new RoutePlanResult("success", [Candidate])),
    };

    [Fact]
    public async Task ExplicitLookupAndSelectionEmitOnlyAPreviewAndRetainEnteredDeparture()
    {
        var count = 0;
        RouteItinerary? selected = null;
        var handler = new StubHandler((_, _) => { count++; return Task.FromResult(Success()); });
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(handler)).Add(component => component.Selected, value => selected = value));
        Assert.Equal(0, count);
        await panel.Find("button").ClickAsync(new());
        Assert.Contains("Scheduled transit", panel.Find(".route-options").TextContent);
        Assert.Null(selected);
        panel.Find(".route-options button").Click();
        Assert.Equal(Candidate.Id, selected!.Id);
        Assert.Equal(Departure, panel.Instance.Request!.DepartureTime);
        Assert.Contains("not saved", panel.Find("[role=status]").TextContent);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ChangedEndpointDiscardsALateRouteResponse()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, _) => { await gate.Task; return Success(); });
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(handler)));
        var lookup = panel.Find("button").ClickAsync(new());
        panel.WaitForAssertion(() => Assert.Contains("Finding a route", panel.Find("[role=status]").TextContent));
        panel.Render(parameters => parameters.Add(component => component.Request, Request with { Destination = new(46m, -74m) }));
        gate.SetResult();
        await lookup;
        Assert.Empty(panel.FindAll(".route-options"));
        Assert.False(panel.Find("button").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData("no_route", "No route found")]
    [InlineData("unsupported", "not supported")]
    public async Task MissingRoutesPreserveManualPlanning(string status, string feedback)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new RoutePlanResult(status, [])),
        }));
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(handler)));
        await panel.Find("button").ClickAsync(new());
        Assert.Contains(feedback, panel.Find("[role=status]").TextContent);
        Assert.Equal(Request, panel.Instance.Request);
        Assert.Empty(panel.FindAll(".route-options"));
    }

    [Fact]
    public async Task RoutingServerRejectionReportsItsProjectSettingsWithoutClaimingLocalSignOut()
    {
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(StubHandler.Status(401))));
        await panel.Find("button").ClickAsync(new());
        Assert.Contains("TravelTracker project settings", panel.Find("[role=status]").TextContent);
        Assert.DoesNotContain("Sign in to preview routes", panel.Find("[role=status]").TextContent);
        Assert.Equal(Request, panel.Instance.Request);
    }

    [Fact]
    public async Task TimedOutPreviewPreservesRequestAndRetriesOnlyAfterExplicitFindRoutes()
    {
        var requests = 0;
        var handler = new StubHandler((_, _) => ++requests == 1
            ? throw new TaskCanceledException("private-network-diagnostic") : Task.FromResult(Success()));
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(handler)));

        await panel.Find("button").ClickAsync(new());

        Assert.Contains("dev tunnel", panel.Find("[role=status]").TextContent);
        Assert.Contains("entered times are unchanged", panel.Find("[role=status]").TextContent);
        Assert.Equal(Request, panel.Instance.Request);
        Assert.Equal(1, requests);
        Assert.False(panel.Find("button").HasAttribute("disabled"));
        Assert.DoesNotContain("private-network-diagnostic", panel.Markup);
        await panel.Find("button").ClickAsync(new());
        Assert.Single(panel.FindAll(".route-options > li"));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task RoutingAuthenticationSetupFailureShowsSpecificSafeProjectGuidance()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = JsonContent.Create(new { status = "authentication_unavailable", message = "private-project-diagnostic" }) }));
        var panel = Render<RoutePreviewPanel>(parameters => parameters.Add(component => component.Request, Request)
            .Add(component => component.Client, Client(handler)));

        await panel.Find("button").ClickAsync(new());

        Assert.Contains("TravelTracker Supabase URL and key", panel.Find("[role=status]").TextContent);
        Assert.DoesNotContain("private-project-diagnostic", panel.Markup);
        Assert.Equal(Request, panel.Instance.Request);
    }

    [Fact]
    public void GeometryDecoderProducesCompleteCoordinates()
    {
        Assert.Equal(new[] { (38.5m, -120.2m), (40.7m, -120.95m), (43.252m, -126.453m) }, RouteGeometry.Decode("_p~iF~ps|U_ulLnnqC_mqNvxq\u0060@"));
    }

    [Theory]
    [InlineData("_")]
    [InlineData("??_")]
    [InlineData("~~~~~~~?")]
    [InlineData("\u0000")]
    public void MalformedGeometryDoesNotProduceAPartialRoute(string encoded) => Assert.Empty(RouteGeometry.Decode(encoded));

    [Fact]
    public void MapLabelsAndDrawsSelectedProviderGeometrySeparatelyFromApproximateLines()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var origin = new TripPlaceDto(Guid.Empty, "attraction", "Start", "", 45.5m, -73.6m);
        var destination = origin with { Name = "End", Lat = 45.6m, Lon = -73.7m };
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [origin, destination])
            .Add(component => component.Legs, [("bus", "Bus")]).Add(component => component.PreviewRoute, Candidate));
        Assert.Single(map.FindAll(".provider-route"));
        Assert.Contains("L", map.Find(".provider-route").GetAttribute("d"));
        Assert.Contains("Other connections remain approximate", map.Markup);
    }

    [Fact]
    public void SelectingADistantAddressCentersItsUnconfirmedPreviewWithoutMovingSavedStops()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var origin = new TripPlaceDto(Guid.Empty, "attraction", "Montreal", "", 45.5m, -73.6m);
        var map = Render<TripMapCanvas>(parameters => parameters.Add(component => component.Stops, [origin]));
        map.Render(parameters => parameters.Add(component => component.PreviewLocation, (48.86m, 2.35m)));
        Assert.Single(map.FindAll(".address-preview-marker"));
        Assert.Equal(origin, Assert.Single(map.Instance.Stops));
        Assert.Contains("translate(384 216)", map.Find(".address-preview-marker").GetAttribute("transform"));
    }
}
