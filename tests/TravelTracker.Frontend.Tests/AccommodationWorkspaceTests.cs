using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Accommodations;
using TravelTracker.Frontend.Tests.Support;

namespace TravelTracker.Frontend.Tests;

/** Component journeys use authenticated session fixtures and HTTP stubs, not the services' internals. */
public sealed class AccommodationWorkspaceTests : BunitContext
{
    static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000421");
    readonly FakeTimeProvider clock = new(DateTimeOffset.Parse("2026-11-08T10:00:00Z"));

    /**
     * Connects the component to the real protected client and deterministic Users/catalogue HTTP responses.
     * @param handler Answers the public service requests.
     * @param write Grants catalogue.write when true.
     * @param read Grants catalogue.read when true.
     * @param usersRead Grants users.read when true.
     * @return The replaceable authentication session.
     */
    SwitchableAuthentication Configure(StubHandler handler, bool write = true, bool read = true, bool usersRead = true)
    {
        var sessions = new SessionRegistry(clock);
        var authentication = new SwitchableAuthentication(sessions);
        authentication.SignIn("test-token", clock.GetUtcNow().AddMinutes(10));
        var scopes = new List<string>();
        if (read) scopes.Add("catalogue.read");
        if (usersRead) scopes.Add("users.read");
        if (write) scopes.Add("catalogue.write");
        ((ClaimsIdentity)authentication.User.Identity!).AddClaim(new Claim(AccessTokenClaims.GrantedScope, string.Join(' ', scopes)));
        var protectedClient = new ProtectedClient(authentication,
            new ServiceDirectory(new Dictionary<string, string>
            {
                ["Accommodations"] = "http://localhost:5106",
                ["Users"] = "https://localhost:5101",
            }),
            new ServiceClient(new HttpClient(handler), NullLogger<ServiceClient>.Instance), clock,
            NullLogger<ProtectedClient>.Instance, sessions);
        Services.AddSingleton<TimeProvider>(clock);
        Services.AddSingleton(sessions);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.AddSingleton(new AccommodationsClient(protectedClient));
        Services.AddSingleton(new UsersClient(protectedClient, new ConfigurationBuilder().Build()));
        Services.AddLogging();
        return authentication;
    }

    StubHandler Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> accommodation, string role = "traveller") =>
        new(async (request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/users/me")
                return Json(new SelfUserProfile(Guid.Parse("00000000-0000-0000-0000-000000000099"), role, "active"));
            return await accommodation(request);
        });

    static HttpResponseMessage Json<T>(T body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(body) };

    [Fact]
    public void SearchThenDetailsSelectsTheServerConfirmedPublicRecord()
    {
        var item = new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "shared");
        Configure(Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(Id.ToString(), StringComparison.Ordinal)
            ? Json(item)
            : Json(new[] { item }))));
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find(".accommodation-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Bay Hotel", page.Find(".accommodation-details").TextContent));
        page.Find(".accommodation-details button").Click();

        Assert.Equal(Id, chosen!.AccommodationId);
        Assert.Contains("Saved place selected for this trip draft", page.Markup);
        Assert.Contains("SHARED", page.Markup);
    }

    [Fact]
    public void CreateUsesPrivateByDefaultAndSelectsOnlyTheConfirmedResponse()
    {
        Dictionary<string, JsonElement>? body = null;
        var created = false;
        var item = new AccommodationDto(Id, "Harbour Stay", "Toronto", 43.6m, -79.4m, "private");
        Configure(Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                body = await request.Content!.ReadFromJsonAsync<Dictionary<string, JsonElement>>();
                created = true;
                return Json(item, HttpStatusCode.Created);
            }
            return Json(created ? new[] { item } : Array.Empty<AccommodationDto>());
        }));
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        Assert.Empty(page.FindAll("#accommodation-visibility"));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change(" Harbour Stay ");
        page.Find("#accommodation-address").Change("Toronto");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Place saved and selected", page.Markup));
        Assert.Equal(Id, chosen!.AccommodationId);
        Assert.Equal("Harbour Stay", body!["name"].GetString());
        Assert.Equal("private", body["visibility"].GetString());
        Assert.Equal(new[] { "address", "lat", "lon", "name", "visibility" }, body.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ReadOnlyGrantCanSearchButCannotOpenCreateForm()
    {
        var item = new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private");
        Configure(Handler(_ => Task.FromResult(Json(new[] { item }))), write: false);
        var page = Render<AccommodationWorkspace>();

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        Assert.Contains("Read-only catalogue", page.Markup);
        Assert.Empty(page.FindAll(".accommodation-heading button"));
    }

    [Fact]
    public void InvalidCoordinatesKeepDraftAndSendNoCreate()
    {
        var writes = 0;
        Configure(Handler(request =>
        {
            if (request.Method == HttpMethod.Post) writes++;
            return Task.FromResult(Json(Array.Empty<AccommodationDto>()));
        }));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Typed lodging");
        page.Find("#accommodation-lat").Change("90.1");
        page.Find("#accommodation-lon").Change("-73");
        page.Find(".accommodation-modal form").Submit();

        Assert.Equal(0, writes);
        Assert.Equal("Typed lodging", page.Find("#accommodation-name").GetAttribute("value"));
        Assert.Equal("true", page.Find("#accommodation-lat").GetAttribute("aria-invalid"));
        Assert.Contains("latitude from −90 to 90", page.Markup);
    }

    [Fact]
    public void ServerValidationKeepsEnteredValuesAndDisplaysFieldErrors()
    {
        Configure(Handler(request => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"errors\":{\"lat\":[\"Latitude is invalid.\"]}}") }
            : Json(Array.Empty<AccommodationDto>()))));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Typed lodging");
        page.Find("#accommodation-lat").Change("43");
        page.Find("#accommodation-lon").Change("-73");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Latitude is invalid", page.Markup));
        Assert.Equal("Typed lodging", page.Find("#accommodation-name").GetAttribute("value"));
        Assert.Equal("43", page.Find("#accommodation-lat").GetAttribute("value"));
        Assert.Equal("true", page.Find("#accommodation-lat").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void HiddenOrRemovedDetailRemovesStaleCardAndShowsGenericUnavailable()
    {
        var item = new AccommodationDto(Id, "Bay Hotel", "Toronto waterfront", 45.5m, -73.5m, "shared");
        var detailCount = 0;
        Configure(Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(Id.ToString(), StringComparison.Ordinal)
            ? Interlocked.Increment(ref detailCount) == 1
                ? Json(item)
                : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"detail\":\"PRIVATE_ADDRESS_SECRET\"}") }
            : Json(new[] { item }))));
        var selectionChanges = new List<AccommodationDto?>();
        var page = Render<AccommodationWorkspace>(parameters => parameters
            .Add(component => component.SelectedAccommodationId, Id)
            .Add(component => component.SelectionChanged, EventCallback.Factory.Create<AccommodationDto?>(this, value => selectionChanges.Add(value))));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find(".accommodation-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Saved place selected", page.Markup));
        page.Find(".accommodation-card button").Click();

        page.WaitForAssertion(() => Assert.Contains("This accommodation is unavailable", page.Markup));
        Assert.DoesNotContain("PRIVATE_ADDRESS_SECRET", page.Markup);
        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.Equal(new AccommodationDto?[] { null }, selectionChanges);
    }

    [Fact]
    public void ForbiddenSearchClearsCachedResultsAndTripDraftSelection()
    {
        var item = new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private");
        var handler = Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(Id.ToString(), StringComparison.Ordinal)
            ? Json(item)
            : Json(new[] { item })));
        Configure(handler);
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find(".accommodation-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Bay Hotel", page.Find(".accommodation-details").TextContent));
        page.Find(".accommodation-details button").Click();
        Assert.Equal(Id, chosen!.AccommodationId);

        handler.RespondWith((int)HttpStatusCode.Forbidden, "{}");
        page.Find(".accommodation-search").Submit();

        page.WaitForAssertion(() => Assert.Contains("You do not have permission", page.Markup));
        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.DoesNotContain("Bay Hotel", page.Find(".accommodation-details").TextContent);
        Assert.Null(chosen);
        Assert.Empty(page.FindAll(".accommodation-error button"));
    }

    [Fact]
    public void ForbiddenDetailClearsAllCachedRecordsAndTripDraftSelection()
    {
        var firstId = Id;
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000422");
        var first = new AccommodationDto(firstId, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private");
        var second = new AccommodationDto(secondId, "Harbour Inn", "Toronto", 43.6m, -79.4m, "shared");
        Configure(Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith(firstId.ToString(), StringComparison.Ordinal)) return Task.FromResult(Json(first));
            if (path.EndsWith(secondId.ToString(), StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
            return Task.FromResult(Json(new[] { first, second }));
        }));
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll(".accommodation-card").Count));
        page.FindAll(".accommodation-card button")[0].Click();
        page.WaitForAssertion(() => Assert.Contains("Bay Hotel", page.Find(".accommodation-details").TextContent));
        page.Find(".accommodation-details button").Click();
        Assert.Equal(firstId, chosen!.AccommodationId);

        page.FindAll(".accommodation-card button")[1].Click();

        page.WaitForAssertion(() => Assert.Contains("You do not have permission", page.Markup));
        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.DoesNotContain("Bay Hotel", page.Markup);
        Assert.DoesNotContain("Harbour Inn", page.Markup);
        Assert.Null(chosen);
        Assert.Empty(page.FindAll(".accommodation-error button"));
    }

    [Fact]
    public void OnlyActiveAdministratorCanChooseSharedVisibility()
    {
        Configure(Handler(_ => Task.FromResult(Json(Array.Empty<AccommodationDto>())), "administrator"));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();

        Assert.Equal(new[] { "private", "shared" }, page.FindAll("#accommodation-visibility option").Select(option => option.GetAttribute("value")));
    }

    [Fact]
    public void DeniedSharedCreateKeepsFormValuesAndStopsOfferingFurtherWrites()
    {
        Configure(Handler(request => Task.FromResult(request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") }
            : Json(Array.Empty<AccommodationDto>())), "administrator"));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Shared stay");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");
        page.Find("#accommodation-visibility").Change("shared");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("You do not have permission", page.Markup));
        Assert.Equal("Shared stay", page.Find("#accommodation-name").GetAttribute("value"));
        Assert.True(page.Find(".accommodation-modal button[type=submit]").HasAttribute("disabled"));

        page.Find(".accommodation-modal button[aria-label='Close add accommodation form']").Click();

        Assert.Empty(page.FindAll("[role=dialog]"));
        Assert.Empty(page.FindAll(".accommodation-heading button"));
        Assert.Contains("Find a place to stay", page.Find("#accommodation-heading").TextContent);
    }

    [Fact]
    public void WriteOnlyCreateKeepsConfirmedSelectionWithoutAttemptingCatalogueRead()
    {
        var posts = 0;
        var searches = 0;
        var item = new AccommodationDto(Id, "Write-only Stay", "Toronto", 43.6m, -79.4m, "private");
        Configure(Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                posts++;
                return Task.FromResult(Json(item, HttpStatusCode.Created));
            }
            searches++;
            return Task.FromResult(Json(Array.Empty<AccommodationDto>()));
        }), read: false, usersRead: false);
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        Assert.Contains("Accommodation read access required", page.Markup);
        Assert.Empty(page.FindAll(".accommodation-results"));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Write-only Stay");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");

        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Place saved and selected for this trip draft", page.Markup));
        Assert.Equal(Id, chosen!.AccommodationId);
        Assert.Equal(1, posts);
        Assert.Equal(0, searches);
        Assert.Empty(page.FindAll("[role=dialog]"));
    }

    [Fact]
    public void ServiceUnavailableRetainsDraftAndOffersRetryForSearch()
    {
        var handler = Handler(_ => Task.FromResult(Json(Array.Empty<AccommodationDto>())));
        Configure(handler);
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find("#accommodation-query").Change("Toronto");
        handler.RespondWith(503, "{}");
        page.Find(".accommodation-search").Submit();

        page.WaitForAssertion(() => Assert.Contains("Service unavailable", page.Markup));
        Assert.Equal("Toronto", page.Find("#accommodation-query").GetAttribute("value"));
        Assert.Single(page.FindAll(".accommodation-error button"));
    }

    [Fact]
    public void UnauthorizedSearchClearsPreviouslyVisibleResultsAndDraftSelection()
    {
        var item = new AccommodationDto(Id, "Bay Hotel", "Toronto", 45.5m, -73.5m, "private");
        var handler = Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(Id.ToString(), StringComparison.Ordinal)
            ? Json(item)
            : Json(new[] { item })));
        Configure(handler);
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find(".accommodation-card button").Click();
        page.WaitForAssertion(() => Assert.Contains("Bay Hotel", page.Find(".accommodation-details").TextContent));
        page.Find(".accommodation-details button").Click();
        Assert.Equal(Id, chosen!.AccommodationId);

        handler.RespondWith((int)HttpStatusCode.Unauthorized, "{}");
        page.Find(".accommodation-search").Submit();

        page.WaitForAssertion(() => Assert.Contains("Sign in to find accommodations", page.Markup));
        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.Null(chosen);
    }

    [Fact]
    public void NetworkFailureDuringCreateKeepsFormAndDoesNotRetryAutomatically()
    {
        var postCount = 0;
        Configure(Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                throw new HttpRequestException("temporary connection problem");
            }
            return Task.FromResult(Json(Array.Empty<AccommodationDto>()));
        }));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Typed lodging");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("save could not be confirmed", page.Markup));
        Assert.Equal(1, postCount);
        Assert.Equal("Typed lodging", page.Find("#accommodation-name").GetAttribute("value"));
        Assert.Equal("43.6", page.Find("#accommodation-lat").GetAttribute("value"));
        Assert.Single(page.FindAll(".accommodation-modal form"));
    }

    [Fact]
    public async Task DuplicateSubmitDuringPendingCreateSendsOnlyOnePost()
    {
        var postStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var createResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var postCount = 0;
        var item = new AccommodationDto(Id, "Pending Stay", "Toronto", 43.6m, -79.4m, "private");
        Configure(Handler(async request =>
        {
            if (request.Method != HttpMethod.Post) return Json(Array.Empty<AccommodationDto>());
            Interlocked.Increment(ref postCount);
            postStarted.TrySetResult();
            var response = await createResponse.Task;
            return response;
        }));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Pending Stay");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");

        var firstSubmit = page.Find(".accommodation-modal form").TriggerEventAsync("onsubmit", new EventArgs());
        await postStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondSubmit = page.Find(".accommodation-modal form").TriggerEventAsync("onsubmit", new EventArgs());
        await secondSubmit;
        Assert.Equal(1, postCount);
        Assert.True(page.Find(".accommodation-modal button[type=submit]").HasAttribute("disabled"));

        createResponse.SetResult(Json(item, HttpStatusCode.Created));
        await firstSubmit;
        page.WaitForAssertion(() => Assert.Contains("Place saved and selected", page.Markup));
    }

    [Fact]
    public async Task SignOutDuringSearchPreventsLateResponseFromRestoringResults()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var searchCount = 0;
        var item = new AccommodationDto(Id, "Old Session Place", "Toronto", 45.5m, -73.5m, "private");
        var auth = Configure(Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath != "/api/v1/accommodations") return Task.FromResult(Json(item));
            if (Interlocked.Increment(ref searchCount) == 1) return Task.FromResult(Json(new[] { item }));
            requestStarted.TrySetResult();
            return lateResponse.Task;
        }));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".accommodation-card")));
        page.Find("#accommodation-query").Change("Old session");

        var pendingSearch = page.Find(".accommodation-search").TriggerEventAsync("onsubmit", new EventArgs());
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        auth.SignOut();
        page.WaitForAssertion(() => Assert.Contains("Sign in to find accommodations", page.Markup));
        lateResponse.TrySetResult(Json(new[] { item with { Name = "Late Session Place" } }));
        await pendingSearch;

        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.DoesNotContain("Late Session Place", page.Markup);
    }

    [Fact]
    public async Task FailedResultsRefreshAfterCreateKeepsConfirmedSelectionWithoutRepeatingPost()
    {
        var postCount = 0;
        var searchCount = 0;
        var item = new AccommodationDto(Id, "Saved Stay", "Toronto", 43.6m, -79.4m, "private");
        Configure(Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                return Task.FromResult(Json(item, HttpStatusCode.Created));
            }
            if (Interlocked.Increment(ref searchCount) > 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") });
            return Task.FromResult(Json(Array.Empty<AccommodationDto>()));
        }));
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Saved Stay");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Place saved and selected. Search results could not be refreshed", page.Markup));
        Assert.Equal(Id, chosen!.AccommodationId);
        Assert.Equal(1, postCount);
        Assert.Single(page.FindAll(".accommodation-error button"));
    }

    [Fact]
    public void ForbiddenResultsRefreshAfterCreateClearsDraftSelectionWithoutRepeatingPost()
    {
        var postCount = 0;
        var searchCount = 0;
        var item = new AccommodationDto(Id, "Saved Stay", "Toronto", 43.6m, -79.4m, "private");
        Configure(Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                postCount++;
                return Task.FromResult(Json(item, HttpStatusCode.Created));
            }
            if (Interlocked.Increment(ref searchCount) > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") });
            return Task.FromResult(Json(Array.Empty<AccommodationDto>()));
        }));
        AccommodationDto? chosen = null;
        var page = Render<AccommodationWorkspace>(parameters => parameters.Add(component => component.SelectionChanged,
            EventCallback.Factory.Create<AccommodationDto?>(this, value => chosen = value)));
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        page.Find("#accommodation-name").Change("Saved Stay");
        page.Find("#accommodation-lat").Change("43.6");
        page.Find("#accommodation-lon").Change("-79.4");
        page.Find(".accommodation-modal form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Place saved, but catalogue access is now denied", page.Markup));
        Assert.Equal(1, postCount);
        Assert.Null(chosen);
        Assert.Empty(page.FindAll(".accommodation-card"));
        Assert.DoesNotContain("Saved Stay", page.Find(".accommodation-details").TextContent);
        Assert.Empty(page.FindAll(".accommodation-error button"));
    }

    [Fact]
    public void AddDialogOpensWithKeyboardPathAndEscapeClosesIt()
    {
        Configure(Handler(_ => Task.FromResult(Json(Array.Empty<AccommodationDto>()))));
        var page = Render<AccommodationWorkspace>();
        page.WaitForAssertion(() => Assert.Contains("No accommodations found", page.Markup));
        page.Find(".accommodation-heading button").Click();
        Assert.Single(page.FindAll("[role=dialog][aria-modal=true]"));

        page.Find(".accommodation-modal").TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(page.FindAll("[role=dialog]"));
    }
}
