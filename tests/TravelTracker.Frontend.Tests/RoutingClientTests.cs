using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies the shared backend contract through production bearer and destination safeguards. */
public sealed class RoutingClientTests
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
    readonly SwitchableAuthentication authentication;

    public RoutingClientTests() => authentication = new(new(clock));

    RoutingClient Client(StubHandler handler, bool signedIn = true)
    {
        if (signedIn) authentication.SignIn("routing-session", clock.GetUtcNow().AddMinutes(10));
        return new(new ProtectedClient(authentication, new(new Dictionary<string, string> { ["Routing"] = "https://routing.example" }),
            new(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, authentication.Sessions));
    }

    static HttpResponseMessage Addresses() => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new AddressLookupResult("success", [new("120 Rue Test", 45.5m, -73.6m, "OpenStreetMap Nominatim")], "© OpenStreetMap contributors")),
    };

    [Fact]
    public async Task ExplicitSearchEncodesOneQueryAndSendsOnlyToConfiguredBackend()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Addresses()));
        var result = await Client(handler).SearchAsync(" Rue Test & #other ");
        Assert.Equal("https://routing.example/v1/places/search?q=Rue%20Test%20%26%20%23other", handler.LastRequest!.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer routing-session", handler.LastRequest.Headers.Authorization!.ToString());
        Assert.Equal("120 Rue Test", Assert.Single(result.Candidates).Label);
    }

    [Fact]
    public async Task ReverseUsesInvariantCoordinatesAndKeepsProviderCoordinatesAsSuggestions()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Addresses()));
        var result = await Client(handler).ReverseAsync(45.501m, -73.601m);
        Assert.Equal("?lat=45.501&lon=-73.601", handler.LastRequest!.RequestUri!.Query);
        Assert.Equal(45.5m, Assert.Single(result.Candidates).Lat);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public async Task InvalidCoordinatesDoNotReachTheBackend(decimal lat, decimal lon)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Addresses()));
        await Assert.ThrowsAsync<ApiError>(() => Client(handler).ReverseAsync(lat, lon));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task AnonymousAddressSearchDoesNotSendARequest()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Addresses()));
        var error = await Assert.ThrowsAsync<ApiError>(() => Client(handler, signedIn: false).SearchAsync("Rue Test"));
        Assert.Equal(401, error.Status);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task SignOutDiscardsAnOtherwiseSuccessfulAddressLookup()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new StubHandler(async (_, _) => { await gate.Task; return Addresses(); });
        var lookup = Client(handler).SearchAsync("Rue Test");
        authentication.SignOut();
        gate.SetResult();
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiError>(() => lookup)).Status);
    }

    [Theory]
    [InlineData("/v1/places/search?q=x&url=https%3A%2F%2Fother.example")]
    [InlineData("/v1/places/reverse?lat=0&lon=0&owner=other")]
    [InlineData("/v1/routes/plan?token=x")]
    [InlineData("/api/v1/users/me")]
    [InlineData("//other.example/v1/places/search?q=x")]
    public void RoutingAllowsOnlyItsFixedPathsAndQueryFields(string path)
    {
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Routing"] = "https://routing.example" });
        Assert.Throws<ApiError>(() => directory.ProtectedUrlFor("Routing", path));
    }

    [Fact]
    public void RemoteRoutingRefusesPlainHttp()
    {
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Routing"] = "http://routing.example" });
        Assert.Throws<ApiError>(() => directory.ProtectedUrlFor("Routing", "/v1/places/search?q=test"));
        Assert.Throws<InvalidOperationException>(() => ServiceDirectory.Validate(new Dictionary<string, string> { ["Routing"] = "http://routing.example" }, false));
    }

    [Fact]
    public async Task RoutePreviewSendsChosenEndpointsModeAndOffsetWithoutSavingATrip()
    {
        RoutePlanRequest? observed = null;
        var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/routes/plan", request.RequestUri!.AbsolutePath);
            observed = await request.Content!.ReadFromJsonAsync<RoutePlanRequest>(token);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new RoutePlanResult("no_route", [])) };
        });
        var chosen = new RoutePlanRequest(new(45.5m, -73.6m), new(45.6m, -73.7m), "bus", DateTimeOffset.Parse("2026-11-23T10:00:00-05:00"));
        var result = await Client(handler).PlanAsync(chosen);
        Assert.Equal(chosen, observed);
        Assert.Equal("no_route", result.Status);
    }

    [Fact]
    public async Task MalformedAddressCoordinatesAreNotOfferedToTheMap()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AddressLookupResult("success", [new("Bad data", 100m, 0m, "Nominatim")], "")),
        }));
        Assert.Equal(FailureKind.Unexpected, (await Assert.ThrowsAsync<ApiError>(() => Client(handler).SearchAsync("Rue Test"))).Kind);
    }
}
