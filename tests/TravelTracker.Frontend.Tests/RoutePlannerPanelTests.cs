using System.Net;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Tests.Support;
using static TravelTracker.Frontend.Tests.Support.RoutingFixtures;

namespace TravelTracker.Frontend.Tests;

/** Exercises explicit route choices, verified mode availability and confirm/cancel without mutating a trip. */
public sealed class RoutePlannerPanelTests : BunitContext
{
    (RoutingClient Routing, TransportClient Catalogue) Clients(StubHandler handler)
    {
        var clock = new FakeTimeProvider(RequestedDeparture.AddHours(-1));
        var authentication = new SwitchableAuthentication(new(clock));
        authentication.SignIn("routing-session", clock.GetUtcNow().AddMinutes(10));
        var client = new ProtectedClient(authentication, new(new Dictionary<string, string>
        {
            ["Routing"] = "https://routing.example", ["Transport"] = "http://localhost:5105",
        }), new(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
            NullLogger<ProtectedClient>.Instance, authentication.Sessions);
        return (new(client), new(client));
    }

    IRenderedComponent<RoutePlannerPanel> Panel(StubHandler handler, Func<RouteSelection, Task<bool>>? confirm = null)
    {
        var clients = Clients(handler);
        return Render<RoutePlannerPanel>(parameters => parameters.Add(component => component.Client, clients.Routing)
            .Add(component => component.Catalogue, clients.Catalogue).Add(component => component.Stops, [Origin, Destination])
            .Add(component => component.InitialRequest, Request).Add(component => component.InitialDestination, 1)
            .Add(component => component.Confirmed, confirm ?? (_ => Task.FromResult(true))));
    }

    static HttpResponseMessage Read(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/v1/routes/capabilities" => Json(Capabilities),
        "/v1/routes/plan" => Json(Success()),
        _ => Json(new[] { Train }),
    };

    static void LoadAndChoose(IRenderedComponent<RoutePlannerPanel> panel)
    {
        panel.WaitForAssertion(() => Assert.False(panel.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        panel.FindAll("button").Single(button => button.TextContent == "Find routes").Click();
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll(".route-options > li")));
        panel.Find(".route-options > li > button").Click();
    }

    [Fact]
    public void OptionsLoadAutomaticallyAndThePaletteModeIsSelectedBeforeADepartureIsEntered()
    {
        var metadataReads = 0;
        var clients = Clients(new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/v1/routes/capabilities") metadataReads++;
            return Task.FromResult(Read(request));
        }));
        var panel = Render<RoutePlannerPanel>(parameters => parameters.Add(component => component.Client, clients.Routing)
            .Add(component => component.Catalogue, clients.Catalogue).Add(component => component.Stops, [Origin, Destination])
            .Add(component => component.InitialMode, "train"));

        panel.WaitForAssertion(() => Assert.False(panel.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Assert.Equal("train", panel.Find("#route-mode").GetAttribute("value"));
        Assert.Equal(1, metadataReads);
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
        panel.Render();
        Assert.Equal(1, metadataReads);
        panel.Find("#route-departure").Change("2026-11-14T10:00:00");
        Assert.False(panel.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
    }

    [Fact]
    public void VerifiedOptionsAndExplicitConfirmationApplyExactlyOneSelectedRoute()
    {
        RouteSelection? accepted = null;
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))),
            selection => { accepted = selection; return Task.FromResult(true); });
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
        LoadAndChoose(panel);
        Assert.Null(accepted);
        Assert.Contains("Intermediate station", panel.Find(".route-leg-details").TextContent);
        Assert.Contains("1 transfer", panel.Find(".route-options").TextContent);
        Assert.Contains("10:07:37", panel.Find(".route-leg-details").TextContent);
        panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").Click();
        panel.WaitForAssertion(() => Assert.NotNull(accepted));
        Assert.Equal(Request, accepted!.Request);
        Assert.Equal(Candidate.Id, accepted.Itinerary.Id);
        Assert.Equal(Train.TransportModeId, accepted.Transport.TransportModeId);
        Assert.Equal((0, 1), (accepted.OriginIndex, accepted.DestinationIndex));
    }

    [Fact]
    public async Task PendingConfirmationLocksCandidateAndClearActionsUntilItCompletes()
    {
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RouteSelection? preview = null;
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))), _ => reply.Task);
        panel.Render(parameters => parameters.Add(component => component.PreviewChanged, value => preview = value));
        LoadAndChoose(panel);
        var selection = preview;
        var confirmation = panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route")
            .TriggerEventAsync("onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        try
        {
            var candidate = panel.Find(".route-options > li > button");
            var clear = panel.FindAll("button").Single(button => button.TextContent == "Clear route preview");
            Assert.True(candidate.HasAttribute("disabled"));
            Assert.True(clear.HasAttribute("disabled"));
            // Already queued events must also be ignored after the controls become disabled.
            await clear.TriggerEventAsync("onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            await candidate.TriggerEventAsync("onclick", new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            Assert.Same(selection, preview);
        }
        finally
        {
            reply.TrySetResult(false);
        }
        await confirmation;
        Assert.False(panel.Find(".route-options > li > button").HasAttribute("disabled"));
        Assert.False(panel.FindAll("button").Single(button => button.TextContent == "Clear route preview").HasAttribute("disabled"));
        Assert.False(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
    }

    [Fact]
    public void CancellingASelectedRouteDoesNotCallTheConfirmationCallback()
    {
        var confirms = 0;
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))),
            _ => { confirms++; return Task.FromResult(true); });
        LoadAndChoose(panel);
        panel.FindAll("button").Single(button => button.TextContent == "Cancel route preview").Click();
        Assert.Equal(0, confirms);
        Assert.Empty(panel.FindAll(".route-options"));
        Assert.Contains("itinerary and entered times are unchanged", panel.Markup);
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("unknown")]
    public void UnsupportedOrUnknownModesCannotBeUsedForRequestsOrConfirmation(string status)
    {
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/routes/capabilities"
            ? Json(Capabilities with { Modes = [new("train", status)] }) : Json(new[] { Train }))));
        panel.WaitForAssertion(() => Assert.Contains(status == "unknown" ? "support unknown" : "unsupported by loaded router", panel.Find(".routing-modes").TextContent));
        Assert.False(panel.Find("#route-mode option[value=train]").HasAttribute("disabled"));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
    }

    [Fact]
    public void MissingSavedCatalogueModePreventsRouteConfirmation()
    {
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/routes/capabilities"
            ? Json(Capabilities) : Json(Array.Empty<TransportDto>()))));
        panel.WaitForAssertion(() => Assert.Contains("saved catalogue mode unavailable", panel.Find(".routing-modes").TextContent));
        Assert.True(panel.Find("#route-mode option[value=train]").HasAttribute("disabled"));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapabilityFailuresExplainTimeoutOrProviderOutageAndAllowAnExplicitReload(bool timeout)
    {
        var failing = true;
        var requests = 0;
        var panel = Panel(new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath != "/v1/routes/capabilities") return Task.FromResult(Read(request));
            requests++;
            if (failing && timeout) throw new TaskCanceledException("private-network-diagnostic");
            return Task.FromResult(failing ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Read(request));
        }));

        panel.WaitForAssertion(() => Assert.Contains(timeout ? "dev tunnel" : "OpenTripPlanner", panel.Markup));
        Assert.Equal(1, requests);
        Assert.True(panel.Find("#route-mode option[value=train]").HasAttribute("disabled"));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
        Assert.DoesNotContain("private-network-diagnostic", panel.Markup);
        failing = false;
        panel.FindAll("button").Single(button => button.TextContent == "Retry routing options").Click();
        panel.WaitForAssertion(() => Assert.False(panel.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        Assert.Equal(2, requests);
    }

    [Fact]
    public void RoutingAuthenticationSetupFailureShowsSpecificSafeProjectGuidance()
    {
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/routes/capabilities"
            ? Json(new { status = "authentication_unavailable", message = "private-project-diagnostic" }, HttpStatusCode.ServiceUnavailable) : Read(request))));


        panel.WaitForAssertion(() => Assert.Contains("TravelTracker Supabase URL and key", panel.Markup));
        Assert.DoesNotContain("private-project-diagnostic", panel.Markup);
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
    }

    [Fact]
    public void ChangedDepartureOrUnconfirmedMarkerDetailsDisableAnOldSelection()
    {
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))));
        LoadAndChoose(panel);
        panel.Render(parameters => parameters.Add(component => component.CanConfirmContext, false));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
        Assert.Contains("Apply or cancel your marker details", panel.Markup);
        panel.Render(parameters => parameters.Add(component => component.CanConfirmContext, true));
        panel.Find("#route-departure").Change("2026-11-14T11:00:00");
        Assert.Empty(panel.FindAll(".route-options"));
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
    }

    [Fact]
    public void NonAdjacentEndpointsCanBePreviewedButCannotSilentlyReorderAndConfirm()
    {
        var confirms = 0;
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))),
            _ => { confirms++; return Task.FromResult(true); });
        var third = Destination with { PlaceId = Guid.NewGuid(), Name = "Third stop at museum" };
        panel.Render(parameters => parameters.Add(component => component.Stops, [Origin, Destination, third]));
        panel.Find("#route-destination").Change("2");
        LoadAndChoose(panel);
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Confirm selected route").HasAttribute("disabled"));
        Assert.Contains("Reorder the itinerary explicitly", panel.Markup);
        Assert.Equal(new[] { Origin, Destination, third }, panel.Instance.Stops);
        Assert.Equal(0, confirms);
    }

    [Fact]
    public void RepeatedTimeRequiresOffsetChoiceAndWindowsZoneIsSentAsIana()
    {
        var panel = Panel(new StubHandler((request, _) => Task.FromResult(Read(request))));
        panel.Render(parameters => parameters.Add(component => component.TimeZone, "Eastern Standard Time"));
        panel.WaitForAssertion(() => Assert.False(panel.Find("#route-mode option[value=train]").HasAttribute("disabled")));
        panel.Find("#route-departure").Change("2026-11-01T01:30:17");
        Assert.True(panel.FindAll("button").Single(button => button.TextContent == "Find routes").HasAttribute("disabled"));
        panel.Find("#route-departure-offset").Change("-04:00:00");
        var request = panel.FindComponent<RoutePreviewPanel>().Instance.Request!;
        Assert.NotNull(request);
        Assert.DoesNotContain("Standard Time", request.TimeZone);
        Assert.Equal(TimeSpan.FromHours(-4), request.DepartureTime.Offset);
        Assert.Equal(17, request.DepartureTime.Second);
        Assert.Equal("Eastern Standard Time", panel.Instance.TimeZone);
    }
}
