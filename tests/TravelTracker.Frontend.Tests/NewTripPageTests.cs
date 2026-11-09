using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components;
using TravelTracker.Frontend.Components.Pages;
using TravelTracker.Frontend.Services;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** The new-trip journey and the saved-trip view against in-process services, through the real protected HTTP stack; authorization here is fake-identity only. */
public sealed class NewTripPageTests : BunitContext
{
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-06T10:00:00Z"));
    readonly FakeTripsBackend backend = new();

    /**
     * wires every service client to the fake backend.
     * @param scope The granted scope on the fake session.
     * @param signedIn Whether the fake identity has an active session.
     * @return The switchable authentication for sign-out scenarios.
     */
    SwitchableAuthentication Configure(string scope = "catalogue.read trips.read trips.write settings.read", bool signedIn = true)
    {
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        if (signedIn) authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        if (signedIn) ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, scope));
        var directory = new ServiceDirectory(new Dictionary<string, string>
        {
            ["Attractions"] = "http://localhost:5104", ["Transport"] = "http://localhost:5105", ["Accommodations"] = "http://localhost:5106",
            ["Settings"] = "http://localhost:5107", ["Trips"] = "http://localhost:5108",
        });
        var client = new ProtectedClient(authentication, directory, new ServiceClient(new HttpClient(backend), NullLogger<ServiceClient>.Instance), clock, NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var attractions = new AttractionsClient(client);
        var transport = new TransportClient(client);
        Services.AddSingleton(new AccommodationsClient(client));
        Services.AddSingleton(attractions);
        Services.AddSingleton(transport);
        Services.AddSingleton(new TripPlacesClient(attractions, transport, client));
        Services.AddSingleton(new TripsClient(client));
        Services.AddSingleton(new SettingsClient(client));
        Services.AddScoped<UserPreferences>();
        Services.AddLogging();
        return authentication;
    }

    static void Search(IRenderedComponent<TripFormPlanner> page, string id, string query)
    {
        page.Find($"#{id}-search").Input(query);
        page.Find($"#{id}-search-button").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.Find($"#{id}-search").Closest("fieldset")!.QuerySelectorAll("ul.place-results button")));
    }

    static void Pick(IRenderedComponent<TripFormPlanner> page, string id, string query, int result = 0)
    {
        Search(page, id, query);
        page.Find($"#{id}-search").Closest("fieldset")!.QuerySelectorAll("ul.place-results button")[result].Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll($"p#{id}")));
    }

    IRenderedComponent<TripFormPlanner> FillBasics(IRenderedComponent<TripFormPlanner>? existing = null)
    {
        var page = existing ?? Render<TripFormPlanner>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#trip-name")));
        page.Find("#trip-name").Input("Paris weekend");
        page.Find("#trip-timezone").Change("Europe/Paris");
        page.Find("#trip-start").Change("2026-06-01T08:00");
        page.Find("#trip-end").Change("2026-06-01T20:00");
        page.Find("form[aria-label='Trip basics']").Submit();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("form[aria-label=Itinerary]")));
        return page;
    }

    static void FillLeg(IRenderedComponent<TripFormPlanner> page, string departure = "2026-06-01T09:00", string arrival = "2026-06-01T10:00")
    {
        Pick(page, "leg-0-from", "Hotel");
        page.Find("#leg-0-to-type").Change("attraction");
        Pick(page, "leg-0-to", "Louvre");
        page.Find("#leg-0-transport").Change(FakeTripsBackend.Bus.ToString());
        page.Find("#leg-0-departure").Change(departure);
        page.Find("#leg-0-arrival").Change(arrival);
    }

    static void GoToReview(IRenderedComponent<TripFormPlanner> page)
    {
        page.Find("form[aria-label=Itinerary]").Submit();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#review-heading")));
    }

    static IElement SaveButton(IRenderedComponent<TripFormPlanner> page) => page.FindAll("button").Single(button => button.TextContent.StartsWith("Save", StringComparison.Ordinal) || button.TextContent == "Saving…");

    [Fact]
    public async Task TripEditorLinksToTheStepByStepFormAndBack()
    {
        Configure();
        JSInterop.Mode = JSRuntimeMode.Loose;
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("new-trip");
        var routes = Render<Routes>();
        var formLink = routes.Find("a[href='new-trip/form']");
        Assert.Equal("Step-by-step form", formLink.TextContent);

        await routes.InvokeAsync(() => navigation.NavigateTo(formLink.GetAttribute("href")!));
        routes.WaitForAssertion(() => Assert.Single(routes.FindAll("#trip-name")));
        var editorLink = routes.Find("main a[href='new-trip']");

        await routes.InvokeAsync(() => navigation.NavigateTo(editorLink.GetAttribute("href")!));
        routes.WaitForAssertion(() => Assert.Equal("Trip editor", routes.Find("h1").TextContent));
        Assert.Single(routes.FindComponents<NewTrip>());
    }

    [Fact]
    public void SavesACompleteTripOnlyAfterConfirmationAndReopensItAfterReload()
    {
        Configure();
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        Assert.Contains("Hotel Lumiere to Louvre by City bus", page.Markup);
        Assert.Empty(backend.CreateBodies);
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.Single(backend.Stored));
        var saved = backend.Stored.Single();
        Assert.Contains($"/trips/{saved.TripId:D}?saved=true", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Equal(FakeTripsBackend.Hotel, saved.Legs[0].From.PlaceId);
        Assert.Equal("2026-06-01T09:00:00.0000000+02:00", saved.Legs[0].Departure.ToString("O"));
        page.Dispose();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/trips/{saved.TripId:D}?saved=true");
        var reopened = Render<TripDetails>(parameters => parameters.Add(p => p.TripId, saved.TripId));
        reopened.WaitForAssertion(() => Assert.Contains("Trip saved.", reopened.Markup));
        Assert.Contains("Paris weekend", reopened.Markup);
        Assert.Contains("Hotel Lumiere", reopened.Markup);
        Assert.Contains("Louvre", reopened.Markup);
        Assert.Contains("2026-06-01 09:00 +02:00", reopened.Markup);
    }

    [Fact]
    public void ShowsFieldErrorsForADisconnectedItineraryAndSendsNothing()
    {
        Configure();
        var page = FillBasics();
        FillLeg(page);
        page.FindAll("button").Single(button => button.TextContent == "Add leg").Click();
        Assert.Contains("Louvre", page.Find("p#leg-1-from").TextContent);
        page.Find("#leg-1-from").Closest("fieldset")!.QuerySelector("button")!.Click();
        page.Find("#leg-1-from-type").Change("attraction");
        Pick(page, "leg-1-from", "Tuileries");
        page.Find("#leg-1-to-type").Change("attraction");
        Pick(page, "leg-1-to", "Louvre");
        page.Find("#leg-1-transport").Change(FakeTripsBackend.Bus.ToString());
        page.Find("#leg-1-departure").Change("2026-06-01T11:00");
        page.Find("#leg-1-arrival").Change("2026-06-01T12:00");
        page.Find("form[aria-label=Itinerary]").Submit();
        page.WaitForAssertion(() => Assert.Contains("This leg must start where the previous leg ended.", page.Markup));
        Assert.NotEmpty(page.FindAll("a[href='#leg-1-from']"));
        Assert.Empty(backend.CreateBodies);
    }

    [Fact]
    public void BadTimesShowFieldErrorsAndNothingIsSent()
    {
        Configure();
        var page = FillBasics();
        FillLeg(page, departure: "2026-06-01T10:00", arrival: "2026-06-01T09:00");
        page.Find("form[aria-label=Itinerary]").Submit();
        page.WaitForAssertion(() => Assert.Contains("Arrival must be after departure.", page.Markup));
        Assert.NotEmpty(page.FindAll("a[href='#leg-0-arrival']"));
        Assert.Equal("true", page.Find("#leg-0-arrival").GetAttribute("aria-invalid"));
        Assert.Empty(backend.CreateBodies);
    }

    [Fact]
    public void SkippedLocalTimeIsReportedNotCorrected()
    {
        Configure();
        var page = FillBasics();
        FillLeg(page, departure: "2026-03-29T02:30", arrival: "2026-03-29T04:00");
        page.Find("form[aria-label=Itinerary]").Submit();
        page.WaitForAssertion(() => Assert.Contains("does not exist in Europe/Paris", page.Markup));
        Assert.Empty(backend.CreateBodies);
    }

    [Fact]
    public void UnavailableServiceKeepsEntriesAndARetrySavesOnce()
    {
        Configure();
        backend.Override = request => request.Method == HttpMethod.Post && request.RequestUri!.Port == 5108 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.Contains("Service unavailable", page.Markup));
        Assert.Contains("Everything you entered is still here", page.Markup);
        Assert.Empty(backend.Stored);
        Assert.Contains("Paris weekend", page.Markup);

        backend.Override = null;
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.Single(backend.Stored));
        Assert.Equal(2, backend.Requests.Count(request => request.Method == HttpMethod.Post && request.Path == "/api/v1/trips"));
    }

    [Fact]
    public void ServiceFieldErrorsReturnToTheLegAndKeepEntries()
    {
        Configure();
        backend.Override = request => request.Method == HttpMethod.Post && request.RequestUri!.Port == 5108
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { errors = new Dictionary<string, string[]> { ["legs[0].arrival"] = ["arrival is after the trip end."] } }) }
            : null;
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("form[aria-label=Itinerary]")));
        Assert.Contains("arrival is after the trip end.", page.Markup);
        Assert.Equal("2026-06-01T10:00", page.Find("#leg-0-arrival").GetAttribute("value"));
        Assert.Empty(backend.Stored);
    }

    [Fact]
    public void RepeatedClicksWhileSavingCreateOneTrip()
    {
        Configure();
        backend.CreateGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.Equal("Saving…", SaveButton(page).TextContent));
        Assert.NotNull(SaveButton(page).GetAttribute("disabled"));
        SaveButton(page).Click();
        SaveButton(page).Click();
        backend.CreateGate.SetResult(true);
        page.WaitForAssertion(() => Assert.Single(backend.Stored));
        Assert.Equal(1, backend.CreateCount);
    }

    [Fact]
    public void LeavingWithUnsavedEntriesAsksFirstAndKeepEditingStays()
    {
        Configure();
        var page = FillBasics();
        var navigation = Services.GetRequiredService<NavigationManager>();
        var start = navigation.Uri;
        page.InvokeAsync(() => navigation.NavigateTo("/history"));
        page.WaitForAssertion(() => Assert.Contains("Leave without saving?", page.Markup));
        Assert.Equal(start, navigation.Uri);
        page.FindAll("button").Single(button => button.TextContent == "Keep editing").Click();
        Assert.DoesNotContain("Leave without saving?", page.Markup);
        Assert.Contains("form", page.Markup);
        page.InvokeAsync(() => navigation.NavigateTo("/history"));
        page.WaitForAssertion(() => Assert.Contains("Leave without saving?", page.Markup));
        page.FindAll("button").Single(button => button.TextContent == "Leave without saving").Click();
        Assert.EndsWith("/history", navigation.Uri);
    }

    [Fact]
    public void LeavingAnEmptyTripNeverAsks()
    {
        Configure();
        var page = Render<TripFormPlanner>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("#trip-name")));
        var navigation = Services.GetRequiredService<NavigationManager>();
        page.InvokeAsync(() => navigation.NavigateTo("/history"));
        Assert.EndsWith("/history", navigation.Uri);
    }

    [Fact]
    public void SignedOutVisitorIsAskedToSignInAndNothingIsRequested()
    {
        Configure(signedIn: false);
        var page = Render<TripFormPlanner>();
        page.WaitForAssertion(() => Assert.Contains("Sign in to plan a trip", page.Markup));
        Assert.Empty(page.FindAll("#trip-name"));
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public void AddingAnAccommodationSelectsTheConfirmedRecord()
    {
        Configure("catalogue.read catalogue.write trips.read trips.write settings.read");
        var page = FillBasics();
        page.Find("#leg-0-from-add").Click();
        page.Find("#leg-0-from-new-name").Change("Pension Nord");
        page.Find("#leg-0-from-new-address").Change("2 Rue Nord");
        page.Find("#leg-0-from-new-lat").Change("48.88");
        page.Find("#leg-0-from-new-lon").Change("2.35");
        page.Find("#leg-0-from-new-save").Click();
        page.WaitForAssertion(() => Assert.Contains("Pension Nord", page.Find("p#leg-0-from").TextContent));
        var body = Assert.Single(backend.AccommodationBodies);
        Assert.Contains("\"visibility\":\"private\"", body);
    }

    [Fact]
    public void AddingAnAccommodationWithBadCoordinatesSendsNothing()
    {
        Configure("catalogue.read catalogue.write trips.read trips.write settings.read");
        var page = FillBasics();
        page.Find("#leg-0-from-add").Click();
        page.Find("#leg-0-from-new-name").Change("Pension Nord");
        page.Find("#leg-0-from-new-lat").Change("999");
        page.Find("#leg-0-from-new-lon").Change("2.35");
        page.Find("#leg-0-from-new-save").Click();
        Assert.Contains("latitude", page.Markup);
        Assert.Empty(backend.AccommodationBodies);
    }

    [Fact]
    public void AddingAnAccommodationNeedsTheCatalogueWriteGrant()
    {
        Configure();
        var page = FillBasics();
        Assert.Empty(page.FindAll("#leg-0-from-add"));
    }

    [Fact]
    public void WithoutTripsWriteTheSaveIsDisabledButPlanningWorks()
    {
        Configure("catalogue.read trips.read");
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        Assert.Contains("trips write permission", page.Markup);
        Assert.NotNull(SaveButton(page).GetAttribute("disabled"));
        Assert.Empty(backend.CreateBodies);
    }

    [Fact]
    public void ForbiddenSaveShowsPermissionDeniedAndKeepsEntries()
    {
        Configure();
        backend.Override = request => request.Method == HttpMethod.Post && request.RequestUri!.Port == 5108 ? new HttpResponseMessage(HttpStatusCode.Forbidden) : null;
        var page = FillBasics();
        FillLeg(page);
        GoToReview(page);
        SaveButton(page).Click();
        page.WaitForAssertion(() => Assert.Contains("Permission denied", page.Markup));
        Assert.Contains("Paris weekend", page.Markup);
    }

    [Fact]
    public void AnotherAccountsTripIsReportedAsNotFound()
    {
        Configure();
        var page = Render<TripDetails>(parameters => parameters.Add(p => p.TripId, Guid.NewGuid()));
        page.WaitForAssertion(() => Assert.Contains("Trip not found", page.Markup));
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void UnavailableTripReadOffersARetry()
    {
        Configure();
        backend.Override = request => request.RequestUri!.Port == 5108 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var page = Render<TripDetails>(parameters => parameters.Add(p => p.TripId, Guid.NewGuid()));
        page.WaitForAssertion(() => Assert.Contains("Service unavailable", page.Markup));
        Assert.NotEmpty(page.FindAll("button"));
    }
}
