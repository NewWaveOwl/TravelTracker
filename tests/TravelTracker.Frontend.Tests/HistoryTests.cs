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
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    [Fact]
    public void HistoryFiltersOrdersAndPagesWithoutChangingSavedTripLinks()
    {
        var trips = Enumerable.Range(1, 12).Select(index =>
            new TripSummaryDto(Guid.NewGuid(), $"Holiday {index:00}", Now.AddDays(-index - 1), Now.AddDays(-index)))
            .Append(new(Guid.NewGuid(), "Upcoming break", Now.AddDays(2), Now.AddDays(4))).ToArray();
        Configure((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(trips) }));
        var page = Render<History>();
        Assert.Equal(10, page.FindAll(".history-card").Count);
        Assert.Equal("Holiday 01", page.Find(".history-card h3").TextContent);
        Assert.Contains("Completed", page.Find(".history-card").TextContent);
        page.FindAll("button").Single(button => button.TextContent == "Next").Click();
        Assert.Equal(3, page.FindAll(".history-card").Count);
        Assert.Contains("Page 2 of 2", page.Markup);

        page.Find("input[type=search]").Input("HOLIDAY 12");
        Assert.Single(page.FindAll(".history-card"));
        Assert.Equal($"map/{trips[11].TripId:D}", page.Find(".history-card a").GetAttribute("href"));
        Assert.DoesNotContain("Page 2", page.Markup);
        page.Find("input[type=search]").Input("nothing matches");
        Assert.Contains("No trips match your filters", page.Markup);
        Assert.DoesNotContain("No saved trips yet", page.Markup);

        page.FindAll("button").Single(button => button.TextContent == "Clear filters").Click();
        page.FindAll("select")[0].Change("upcoming");
        Assert.Equal("Upcoming break", page.Find(".history-card h3").TextContent);
        page.FindAll("select")[0].Change("all");
        page.FindAll("input[type=date]")[0].Change("2026-11-10");
        page.FindAll("input[type=date]")[1].Change("2026-11-11");
        Assert.Equal(new[] { "Holiday 01", "Holiday 02" }, page.FindAll(".history-card h3").Select(heading => heading.TextContent));
        page.FindAll("input[type=date]")[1].Change("2026-11-01");
        Assert.Contains("From date must be on or before", page.Find("[role=alert]").TextContent);
        Assert.Empty(page.FindAll(".history-card"));
        page.FindAll("button").Single(button => button.TextContent == "Clear filters").Click();
        page.FindAll("select")[1].Change("oldest");
        Assert.Equal("Holiday 12", page.Find(".history-card h3").TextContent);
    }

    [Fact]
    public void ExpandingTripLoadsPrivateDetailsAndStatusChangeUpdatesHistory()
    {
        var trip = new TripSummaryDto(Guid.NewGuid(), "Weekend", Now.AddDays(2), Now.AddDays(3));
        var stop = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Hotel", "Private address", 10m, 20m);
        var detail = new TripDto(trip.TripId, trip.Name, "America/Toronto", trip.Start, trip.End, false, [], [stop]);
        var writes = 0;
        Configure(async (request, token) =>
        {
            if (request.Method == HttpMethod.Put)
            {
                Assert.EndsWith($"/{trip.TripId:D}/status", request.RequestUri!.AbsolutePath);
                Assert.Contains("completed", await request.Content!.ReadAsStringAsync(token));
                writes++;
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { status = "completed" }) };
            }
            return new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("/trips")
                ? JsonContent.Create(new[] { trip }) : JsonContent.Create(detail) };
        }, canWrite: true);
        var page = Render<History>();
        Assert.Empty(page.FindAll(".history-detail"));
        page.Find(".history-toggle").Click();
        Assert.Contains("Hotel", page.Find(".history-detail").TextContent);
        Assert.DoesNotContain(stop.Address, page.Find(".history-detail").TextContent);
        page.Find(".history-detail select").Change("completed");
        Assert.Equal(1, writes);
        Assert.Equal("Completed", page.Find(".history-status").TextContent);
        Assert.Equal($"trips/{trip.TripId:D}", page.Find(".history-detail a").GetAttribute("href"));
        page.Find(".history-toggle").DoubleClick();
        Assert.EndsWith($"/trips/{trip.TripId:D}", Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().Uri);
        page.Find(".history-toggle").Click();
        Assert.Empty(page.FindAll(".history-detail"));
    }

    [Fact]
    public void SharePublishesOnlyDistinctItineraryPlacesAndRetriesUnconfirmedOnes()
    {
        var summary = new TripSummaryDto(Guid.NewGuid(), "Weekend", Now, Now.AddDays(1));
        var hotel = new TripPlaceDto(Guid.NewGuid(), "accommodation", "Hotel", "", 10m, 20m);
        var museum = hotel with { PlaceId = Guid.NewGuid(), Type = "attraction", Name = "Museum" };
        var station = hotel with { PlaceId = Guid.NewGuid(), Type = "transport_stop", Name = "Station" };
        var leg = new TripDto.Leg(1, hotel, museum, new(Guid.NewGuid(), "train", "Train"), Now, Now.AddHours(1));
        var detail = new TripDto(summary.TripId, summary.Name, "UTC", summary.Start, summary.End, false,
            [leg], [hotel, museum, station, hotel]);
        var writes = new List<string>();
        Configure((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content =
                    request.RequestUri!.AbsolutePath.EndsWith("/trips") ? JsonContent.Create(new[] { summary }) : JsonContent.Create(detail) });
            Assert.Equal(HttpMethod.Post, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            writes.Add(path);
            return Task.FromResult(writes.Count == 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { visibility = "shared" }) });
        }, canShare: true);
        var page = Render<History>();
        page.Find(".history-toggle").Click();
        Assert.Empty(writes);
        Assert.Single(page.FindAll(".history-share"));
        page.Find(".history-share button").Click();
        var expected = new[] { $"/api/v1/accommodations/{hotel.PlaceId:D}/share",
            $"/api/v1/attractions/{museum.PlaceId:D}/share", $"/api/v1/route-points/{station.PlaceId:D}/share" };
        Assert.Equal(expected, writes);
        Assert.Contains("Museum", page.Find(".history-share [role=alert]").TextContent);
        page.Find(".history-share button").Click();
        Assert.Equal(expected.Append(expected[1]), writes);
        Assert.True(page.Find(".history-share button").HasAttribute("disabled"));
    }

    [Fact]
    public async Task HistoryShowsLoadingThenRecoverableErrorThenIntentionalEmptyState()
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = true;
        Configure((_, _) => failed ? response.Task
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<TripSummaryDto>()) }));
        var page = Render<History>();
        Assert.Contains("Loading your saved trips", page.Markup);
        Assert.Empty(page.FindAll(".history-card"));
        response.SetResult(new(HttpStatusCode.ServiceUnavailable));
        await page.InvokeAsync(() => Task.CompletedTask);
        page.WaitForAssertion(() => Assert.Contains("Unable to load your saved trips", page.Markup));
        failed = false;
        page.Find("button").Click();
        page.WaitForAssertion(() => Assert.Contains("No saved trips yet", page.Markup));
        Assert.Equal("trip", page.Find("a").GetAttribute("href"));
    }

    [Fact]
    public void SignedOutHistoryDoesNotRequestPrivateTrips()
    {
        var calls = 0;
        Configure((_, _) => { calls++; throw new InvalidOperationException("No session must mean no request."); }, signedIn: false);
        var page = Render<History>();
        Assert.Contains("Sign in to see your trips", page.Markup);
        Assert.Equal(0, calls);
    }

    /**
     * Uses the real protected HTTP client with deterministic history data and session time.
     * @param respond Supplies the isolated service response.
     * @param signedIn Whether an authenticated session is available.
     */
    void Configure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, bool signedIn = true, bool canWrite = false, bool canShare = false)
    {
        var clock = new FakeTimeProvider(Now);
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        if (signedIn) authentication.SignIn("history-token", Now.AddMinutes(10), "trips.read" + (canWrite ? " trips.write" : "") + (canShare ? " catalogue.write" : ""));
        var directory = new ServiceDirectory(new Dictionary<string, string> {
            ["Trips"] = "http://localhost:5108", ["Accommodations"] = "http://localhost:5106",
            ["Attractions"] = "http://localhost:5104", ["Transport"] = "http://localhost:5105" });
        var service = new ServiceClient(new HttpClient(new StubHandler(respond)), NullLogger<ServiceClient>.Instance);
        var client = new ProtectedClient(authentication, directory, service, clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new TripPlaceSharingClient(client));
    }

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
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(new TripsClient(client));

        var page = Render<History>();
        page.WaitForAssertion(() => Assert.Equal($"map/{trip.TripId:D}",
            page.Find("ul[aria-label='Saved trips'] a").GetAttribute("href")));
        Assert.Contains(trip.Name, page.Markup);
        page.Find("input[type=search]").Input("Weekend");

        await page.InvokeAsync(authentication.SignOut);

        page.WaitForAssertion(() => Assert.Contains("Sign in to see your trips", page.Markup));
        Assert.DoesNotContain(trip.Name, page.Markup);
    }
}
