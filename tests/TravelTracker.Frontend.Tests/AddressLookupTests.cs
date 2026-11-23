using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Exercises address loading, manual edits and stale-response rejection through the real HTTP client. */
public sealed class AddressLookupTests : BunitContext
{
    RoutingClient Client(StubHandler handler, bool signedIn = true)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
        var authentication = new SwitchableAuthentication(new(clock));
        if (signedIn) authentication.SignIn("routing-session", clock.GetUtcNow().AddMinutes(10));
        return new(new ProtectedClient(authentication, new(new Dictionary<string, string> { ["Routing"] = "https://routing.example" }),
            new(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, authentication.Sessions));
    }

    static HttpResponseMessage Match(string label = "120 Rue Test") => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new AddressLookupResult("success", [new(label, 45.5m, -73.6m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
    };

    [Fact]
    public async Task TypingDoesNotSearchAndSubmittingShowsSpinnerInsideAddressBoxUntilResponse()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var handler = new StubHandler(async (_, _) => { count++; await gate.Task; return Match(); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler)));
        field.Find("input").Input("Rue Test");
        Assert.Equal(0, count);
        var search = field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-input-box .address-spinner")));
        Assert.Equal("true", field.Find(".address-input-box").GetAttribute("aria-busy"));
        Assert.Contains("Waiting for address", field.Find("[role=status]").TextContent);
        gate.SetResult();
        await search;
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-candidates button")));
        Assert.Empty(field.FindAll(".address-spinner"));
        Assert.Equal("Rue Test", field.Find("input").GetAttribute("value"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void ReverseSuggestionChangesOnlyEditableAddressAndNeverSelectsDifferentCoordinates()
    {
        string? address = null;
        AddressCandidate? selected = null;
        var handler = new StubHandler((_, _) => Task.FromResult(Match()));
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.Latitude, 45.501m).Add(component => component.Longitude, -73.601m)
            .Add(component => component.ReverseVersion, 1)
            .Add(component => component.ValueChanged, value => address = value)
            .Add(component => component.CandidateSelected, value => selected = value));
        field.WaitForAssertion(() => Assert.Equal("120 Rue Test", address));
        Assert.Null(selected);
        Assert.Contains("Review or edit", field.Find("[role=status]").TextContent);
        Assert.Equal(45.501m, field.Instance.Latitude);
        Assert.Equal(-73.601m, field.Instance.Longitude);
    }

    [Fact]
    public async Task ManualEditCancelsQueuedReverseLookupAndRejectsLateSuggestion()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? address = null;
        var handler = new StubHandler(async (_, _) => { await gate.Task; return Match("Late address"); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.Latitude, 45.5m).Add(component => component.Longitude, -73.6m)
            .Add(component => component.ReverseVersion, 1).Add(component => component.ValueChanged, value => address = value));
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-spinner")));
        field.Find("input").Input("My manual address");
        gate.SetResult();
        await field.InvokeAsync(() => Task.CompletedTask);
        field.WaitForAssertion(() => Assert.Equal("My manual address", address));
        Assert.Equal("My manual address", field.Find("input").GetAttribute("value"));
        Assert.Empty(field.FindAll(".address-spinner"));
    }

    [Fact]
    public async Task MovingToAnotherStopRejectsOldLookupAndAcceptsTheNewOne()
    {
        var oldGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (request, _) =>
        {
            if (request.RequestUri!.Query.Contains("lat=45.5&")) { await oldGate.Task; return Match("Old address"); }
            return Match("New address");
        });
        string? address = null;
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.Context, new object()).Add(component => component.Latitude, 45.5m)
            .Add(component => component.Longitude, -73.6m).Add(component => component.ReverseVersion, 1)
            .Add(component => component.ValueChanged, value => address = value));
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-spinner")));
        field.Render(parameters => parameters.Add(component => component.Context, new object())
            .Add(component => component.Latitude, 45.6m).Add(component => component.ReverseVersion, 2));
        field.WaitForAssertion(() => Assert.Equal("New address", address));
        oldGate.SetResult();
        await field.InvokeAsync(() => Task.CompletedTask);
        Assert.Equal("New address", field.Find("input").GetAttribute("value"));
    }

    [Theory]
    [InlineData(429, "busy")]
    [InlineData(503, "unavailable")]
    [InlineData(401, "TravelTracker project settings")]
    public async Task FailedSearchPreservesManualAddressAndClearsSpinner(int status, string feedback)
    {
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(StubHandler.Status(status))));
        field.Find("input").Input("My manual address");
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Contains(feedback, field.Find("[role=status]").TextContent);
        Assert.Equal("My manual address", field.Find("input").GetAttribute("value"));
        Assert.Empty(field.FindAll(".address-spinner"));
        Assert.Single(field.FindAll("a[href='https://www.openstreetmap.org/copyright']"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimedOutSearchOrReverseKeepsManualInputAndAllowsAnExplicitRetry(bool reverse)
    {
        var requests = 0;
        var handler = new StubHandler((_, _) => { requests++; throw new TaskCanceledException("private-network-diagnostic"); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.Value, "My manual address").Add(component => component.Latitude, 45.5m)
            .Add(component => component.Longitude, -73.6m).Add(component => component.ReverseVersion, reverse ? 1 : 0));
        if (!reverse) await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);

        field.WaitForAssertion(() => Assert.Contains("dev tunnel", field.Find("[role=status]").TextContent));
        Assert.Equal("My manual address", field.Find("input").GetAttribute("value"));
        Assert.Empty(field.FindAll(".address-spinner"));
        Assert.False(field.Find("button").HasAttribute("disabled"));
        Assert.Equal(1, requests);
        Assert.DoesNotContain("private-network-diagnostic", field.Markup);
    }

    [Fact]
    public async Task RoutingAuthenticationSetupFailureShowsSpecificSafeProjectGuidance()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        { Content = JsonContent.Create(new { status = "authentication_unavailable", message = "private-project-diagnostic" }) }));
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler)));
        field.Find("input").Input("My manual address");

        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);

        Assert.Contains("TravelTracker Supabase URL and key", field.Find("[role=status]").TextContent);
        Assert.Equal("My manual address", field.Find("input").GetAttribute("value"));
        Assert.DoesNotContain("private-project-diagnostic", field.Markup);
        Assert.Empty(field.FindAll(".address-spinner"));
    }

    [Fact]
    public async Task MissingLocalSessionAsksForSignInWithoutCallingTheRoutingServer()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Anonymous address lookup must not send a request."));
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler, signedIn: false)));
        field.Find("input").Input("Rue Test");
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Contains("Sign in to look up addresses", field.Find("[role=status]").TextContent);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task ChoosingSearchCandidateExplicitlyPreviewsItWithoutAnotherLookup()
    {
        var count = 0;
        AddressCandidate? chosen = null;
        var handler = new StubHandler((_, _) => { count++; return Task.FromResult(Match()); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.CandidateSelected, value => chosen = value));
        field.Find("input").Input("Rue Test");
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Null(chosen);
        field.Find(".address-candidates button").Click();
        Assert.Equal("120 Rue Test", chosen!.Label);
        Assert.Equal(45.5m, chosen.Lat);
        Assert.Equal(1, count);
        Assert.Empty(field.FindAll(".address-candidates"));
    }

    [Fact]
    public async Task CancelLookupKeepsTheInputAndDiscardsAnIgnoringProviderResponse()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, _) => { await gate.Task; return Match(); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler)));
        field.Find("input").Input("Rue Test");
        var lookup = field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-spinner")));
        field.FindAll("button").Single(button => button.TextContent == "Cancel lookup").Click();
        gate.SetResult();
        await lookup;
        Assert.Equal("Rue Test", field.Find("input").GetAttribute("value"));
        Assert.Contains("cancelled", field.Find("[role=status]").TextContent);
        Assert.Empty(field.FindAll(".address-candidates"));
    }

    [Fact]
    public async Task NoMatchKeepsTheManualAddressAndCoordinates()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AddressLookupResult("no_match", [], "© OpenStreetMap contributors")),
        }));
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.Value, "My manual address").Add(component => component.Latitude, 45.5m)
            .Add(component => component.Longitude, -73.6m));
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Contains("No matching address", field.Find("[role=status]").TextContent);
        Assert.Equal("My manual address", field.Find("input").GetAttribute("value"));
        Assert.Equal(45.5m, field.Instance.Latitude);
        Assert.Equal(-73.6m, field.Instance.Longitude);
    }

    [Fact]
    public async Task KeyboardArrowsMoveAcrossAmbiguousCandidatesWithoutConfirmingAndEscapeCancels()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddressCandidate? selected = null;
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AddressLookupResult("success",
                [new("Rue Test, Montreal", 45.5m, -73.6m, "OpenStreetMap Nominatim"),
                 new("Rue Test, Laval", 45.6m, -73.7m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
        }));
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler))
            .Add(component => component.CandidateSelected, value => selected = value));
        field.Find("input").Input("Rue Test");
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Equal(2, field.FindAll(".address-candidates button").Count);
        Assert.Contains("2 matching addresses", field.Find("[role=status]").TextContent);
        field.Find("input").KeyDown("ArrowDown");
        var firstFocus = JSInterop.Invocations.Last(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal)).Arguments[0];
        field.FindAll(".address-candidates button")[0].KeyDown("End");
        var lastFocus = JSInterop.Invocations.Last(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal)).Arguments[0];
        Assert.NotEqual(firstFocus, lastFocus);
        Assert.Null(selected);
        field.FindAll(".address-candidates button")[1].KeyDown("ArrowDown");
        Assert.Equal(firstFocus, JSInterop.Invocations.Last(call => call.Identifier.EndsWith("focus", StringComparison.Ordinal)).Arguments[0]);
        field.FindAll(".address-candidates button")[0].KeyDown("Escape");
        Assert.Empty(field.FindAll(".address-candidates"));
        Assert.Null(selected);
        Assert.Equal("Rue Test", field.Find("input").GetAttribute("value"));
        Assert.Contains("cancelled", field.Find("[role=status]").TextContent);
    }

    [Fact]
    public async Task RepeatedSubmitDuringLookupDoesNotSendOrRestartAnotherRequest()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var handler = new StubHandler(async (_, _) => { count++; await gate.Task; return Match(); });
        var field = Render<AddressLookup>(parameters => parameters.Add(component => component.Client, Client(handler)));
        field.Find("input").Input("Rue Test");
        var first = field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        field.WaitForAssertion(() => Assert.Single(field.FindAll(".address-spinner")));
        await field.Find("form").TriggerEventAsync("onsubmit", EventArgs.Empty);
        Assert.Equal(1, count);
        gate.SetResult();
        await first;
        Assert.Single(field.FindAll(".address-candidates button"));
    }
}
