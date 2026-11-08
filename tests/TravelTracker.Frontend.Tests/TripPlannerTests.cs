using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Verifies the shared draft and the Trips HTTP contract through rendered planner controls. */
public sealed class TripPlannerTests : BunitContext
{
    static readonly Guid TripId = Guid.Parse("a4fc96df-c35b-4305-92d8-4ae86638b585");
    static readonly TripPlaceDto Hotel = new(Guid.NewGuid(), "accommodation", "Hotel", "", 45.50m, -73.57m);
    static readonly TripPlaceDto Museum = new(Guid.NewGuid(), "attraction", "Museum", "", 45.51m, -73.58m);
    static readonly TripDto.Mode Train = new(Guid.NewGuid(), "train", "Train");
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-14T09:00:00-05:00");
    static readonly TripDto Saved = new(TripId, "Weekend", "America/Toronto", Start, Start.AddHours(4),
        false, [new(1, Hotel, Museum, Train, Start.AddHours(1), Start.AddHours(2))]);

    [Fact]
    public void FailedUpdateKeepsDraftForRetryAndFreshTripDoesNotReuseSavedId()
    {
        var writes = new List<SaveTripRequest>();
        Configure(async (request, token) =>
        {
            Assert.Equal($"/api/v1/trips/{TripId:D}", request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
                return Json(Saved);

            Assert.Equal(HttpMethod.Put, request.Method);
            var body = (await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token))!;
            writes.Add(body);
            return writes.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Json(Saved with { Name = body.Name });
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/map/{TripId:D}");
        var page = Render<NewTrip>(parameters => parameters.Add(editor => editor.TripId, TripId));

        Click(page, "Standard editor");
        page.Find(".trip-basics input").Change("Updated weekend");
        Click(page, "Review on map");
        Assert.Contains("Updated weekend", page.Find("[aria-label='Trip review details']").TextContent);
        Assert.Equal(new[] { "01HotelAccommodation", "02MuseumAttraction" },
            page.FindAll(".trip-stop").Select(stop => string.Concat(stop.TextContent.Where(c => !char.IsWhiteSpace(c)))));
        Click(page, "Save trip");
        page.WaitForAssertion(() => Assert.Contains("entries are retained", page.Find("[role=alert]").TextContent));
        Click(page, "Retry save");
        page.WaitForAssertion(() => Assert.Contains("Saved Updated weekend", page.Find("[role=status]").TextContent));
        Assert.Equal(2, writes.Count);
        Assert.Equal(writes[0].Legs.Single(), writes[1].Legs.Single());
        Assert.Equal(Train.TransportModeId, writes[1].Legs.Single().TransportModeId);

        navigation.NavigateTo("/new-trip");
        page.Render(parameters => parameters.Add(editor => editor.TripId, (Guid?)null));
        Assert.Equal("Trip editor", page.Find("h1").TextContent);
        Assert.Empty(page.FindAll(".trip-stop"));
        Assert.Equal("", page.Find(".trip-basics input").GetAttribute("value"));
    }

    [Fact]
    public void MapPickedStopsAndLegStayInTheDraftWhenSwitchingToStandardEditor()
    {
        Configure((_, _) => throw new InvalidOperationException("Drafting must not write to a catalogue."));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/map");
        var page = Render<NewTrip>();

        Click(page, "Accommodation");
        Click(page, "Pick a location on the map");
        page.Find(".trip-place input[type=text]").Change("My hotel");
        Click(page, "Pick map center");
        page.Find(".trip-place").Submit();

        Click(page, "Attraction");
        Click(page, "Pick a location on the map");
        page.Find(".trip-place input[type=text]").Change("My museum");
        page.Find("[aria-label='Pan east']").Click();
        Click(page, "Pick map center");
        page.Find(".trip-place").Submit();
        Click(page, "Walk");
        Click(page, "Standard editor");

        Assert.Equal(2, page.FindAll(".trip-stop").Count);
        Assert.Contains("Walk", page.Find(".trip-leg-time").TextContent);
        Assert.Empty(page.FindAll(".trip-marker"));
        Click(page, "Review on map");
        Assert.Equal(2, page.FindAll(".trip-marker").Count);
        Assert.Contains("Enter a trip name", page.Find("[role=alert]").TextContent);
    }

    [Fact]
    public void MapPlannerCreatesATripFromSavedPlacesAndReusesTheReturnedId()
    {
        SaveTripRequest? created = null;
        Configure(async (request, token) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            switch (path)
            {
                case "/api/v1/accommodations":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AccommodationId = Hotel.PlaceId, Hotel.Name, Hotel.Address, Hotel.Lat, Hotel.Lon } }) };
                case "/api/v1/attractions":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(
                        new[] { new { AttractionId = Museum.PlaceId, Museum.Name, Museum.Address, Museum.Lat, Museum.Lon, EstimatedVisitMinutes = 30 } }) };
                case "/api/v1/transport-modes":
                    Assert.Contains("code=train", request.RequestUri.Query);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { Train }) };
                case "/api/v1/trips":
                    Assert.Equal(HttpMethod.Post, request.Method);
                    created = await request.Content!.ReadFromJsonAsync<SaveTripRequest>(token);
                    return Json(Saved);
                default:
                    throw new InvalidOperationException($"Unexpected request path {path}.");
            }
        });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/map");
        var page = Render<NewTrip>();

        page.Find(".trip-basics input").Change("Weekend");
        page.FindAll(".trip-basics input[type=datetime-local]")[0].Change("2026-11-14T09:00");
        page.FindAll(".trip-basics input[type=datetime-local]")[1].Change("2026-11-14T13:00");
        Click(page, "Accommodation");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Hotel", page.Find(".trip-search-results").TextContent));
        Click(page, "Hotel");
        Click(page, "Attraction");
        page.Find(".trip-search").Submit();
        page.WaitForAssertion(() => Assert.Contains("Museum", page.Find(".trip-search-results").TextContent));
        Click(page, "Museum");
        Click(page, "Train");
        page.FindAll(".trip-panel input[type=datetime-local]")[0].Change("2026-11-14T10:00");
        page.FindAll(".trip-panel input[type=datetime-local]")[1].Change("2026-11-14T11:00");
        Click(page, "Review on map");
        Assert.True(page.FindAll("[role=alert]").Count == 0, string.Join(" ", page.FindAll("[role=alert]").Select(alert => alert.TextContent)));
        Click(page, "Save trip");

        page.WaitForAssertion(() => Assert.Contains("Saved Weekend", page.Find("[role=status]").TextContent));
        Assert.NotNull(created);
        Assert.Equal(Start, created.Start);
        var leg = Assert.Single(created.Legs);
        Assert.Equal(Hotel.PlaceId, leg.From.PlaceId);
        Assert.Equal(Museum.PlaceId, leg.To.PlaceId);
        Assert.Equal(Train.TransportModeId, leg.TransportModeId);
        Assert.EndsWith($"/map/{TripId:D}", navigation.Uri);
    }

    void Configure(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var clock = new FakeTimeProvider(Start);
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", Start.AddHours(1));
        var directory = new ServiceDirectory(new Dictionary<string, string>
        {
            ["Trips"] = "http://localhost:5108",
            ["Accommodations"] = "http://localhost:5106",
            ["Attractions"] = "http://localhost:5104",
            ["Transport"] = "http://localhost:5105"
        });
        var services = new ServiceClient(new HttpClient(new StubHandler(respond)), NullLogger<ServiceClient>.Instance);
        var client = new ProtectedClient(authentication, directory, services, clock, NullLogger<ProtectedClient>.Instance, sessions);
        var transport = new TransportClient(client);
        Services.AddSingleton(transport);
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new TripPlacesClient(new AttractionsClient(client), transport, client));
    }

    static HttpResponseMessage Json(TripDto value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    static void Click(IRenderedComponent<NewTrip> page, string label) =>
        page.FindAll("button").Single(button => button.TextContent.Trim() == label).Click();
}
