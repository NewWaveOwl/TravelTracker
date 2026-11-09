using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies the saved-history route against the protected Trips HTTP contract. */
public sealed class HistoryTests : BunitContext
{
    [Fact]
    public async Task HistoryOpensSavedMapAndClearsPrivateEntriesOnSignOut()
    {
        var start = DateTimeOffset.Parse("2026-11-06T10:00:00Z");
        var trip = new TripSummaryDto(Guid.Parse("d03eaef8-a64e-4c88-80b8-32a047e4241b"), "Weekend in Montréal", start, start.AddDays(2));
        using var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/v1/trips", request.RequestUri?.AbsolutePath);
            Assert.Equal("history-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { trip }) });
        });
        using var http = new HttpClient(handler);
        var clock = new FakeTimeProvider(start);
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("history-token", start.AddMinutes(10), "trips.read");
        var directory = new ServiceDirectory(new Dictionary<string, string> { ["Trips"] = "http://localhost:5108" });
        var client = new ProtectedClient(authentication, directory, new ServiceClient(http, NullLogger<ServiceClient>.Instance),
            clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new TripsClient(client));

        var page = Render<History>();
        page.WaitForAssertion(() => Assert.Equal($"map/{trip.TripId:D}",
            page.Find("ul[aria-label='Saved trips'] a").GetAttribute("href")));
        Assert.Contains(trip.Name, page.Markup);

        await page.InvokeAsync(authentication.SignOut);

        page.WaitForAssertion(() => Assert.Contains("Sign in to see your trips", page.Markup));
        Assert.DoesNotContain(trip.Name, page.Markup);
    }
}
