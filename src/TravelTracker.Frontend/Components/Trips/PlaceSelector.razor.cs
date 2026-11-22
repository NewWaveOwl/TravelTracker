using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Components.Trips;

/** Picks one place from its owning catalogue service and reports it by its public identifier. */
public partial class PlaceSelector : IDisposable
{
    [Inject] AccommodationsClient Accommodations { get; set; } = default!;
    [Inject] AttractionsClient Attractions { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] ILogger<PlaceSelector> Logger { get; set; } = default!;

    /** Visible name of the field group. */
    [Parameter, EditorRequired] public string Label { get; set; } = "";

    /** Element id of the chosen-place text, so an error summary can link to it. */
    [Parameter, EditorRequired] public string Id { get; set; } = "";

    /** Kinds the traveller may pick from: accommodation, attraction and transport_stop. */
    [Parameter] public string[] Types { get; set; } = ["accommodation", "attraction", "transport_stop"];
    [Parameter] public bool CanCreateTransportStops { get; set; }

    /** The current choice. */
    [Parameter] public TripPlaceChoice? Value { get; set; }

    /** Raised with the new choice. */
    [Parameter] public EventCallback<TripPlaceChoice> ValueChanged { get; set; }

    /** The field error to show under the group. */
    [Parameter] public string? Error { get; set; }

    string type = "";
    string query = "";
    bool busy, searched, changing, adding, canWriteCatalogue;
    string? failure;
    IReadOnlyList<TripPlaceChoice> results = [];
    readonly CancellationTokenSource lifetime = new();

    bool CanAdd => canWriteCatalogue && type == "accommodation";

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(Authentication.GetAuthenticationStateAsync());
    }

    /**
     * rechecks the catalogue write hint when the signed-in account or its scopes change.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = RefreshAccessAsync(state);

    async Task RefreshAccessAsync(Task<AuthenticationState> state)
    {
        try
        {
            await InvokeAsync(async () =>
            {
                await UpdateAccessAsync(state);
                StateHasChanged();
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError(exception, "Refreshing the catalogue write hint failed");
        }
    }

    async Task UpdateAccessAsync(Task<AuthenticationState> state)
    {
        var user = (await state).User;
        // a revoked or expired session must not keep the add control visible, so the scope check needs the live session too
        canWriteCatalogue = AccessTokenClaims.IsSignedIn(user, Sessions, Clock.GetUtcNow()) && AccessTokenClaims.GrantedScopes(user).Contains("catalogue.write");
        if (!canWriteCatalogue) adding = false;
    }

    protected override void OnParametersSet()
    {
        if (type.Length == 0 || !Types.Contains(type)) type = Types[0];
    }

    /** Drops results and messages from the previous kind so they cannot be picked for the new one. */
    void ClearResults()
    {
        results = [];
        searched = false;
        failure = null;
    }

    static string TypeLabel(string kind) => kind switch
    {
        "accommodation" => "Accommodation",
        "attraction" => "Attraction",
        _ => "Transport stop",
    };

    async Task KeyPressedAsync(KeyboardEventArgs eventArgs)
    {
        if (eventArgs.Key == "Enter") await SearchAsync();
    }

    async Task SearchAsync()
    {
        if (busy) return;
        busy = true;
        failure = null;
        var requested = type;
        try
        {
            var found = requested switch
            {
                "accommodation" => (await Accommodations.SearchAsync(query, lifetime.Token)).Select(item => new TripPlaceChoice(item.AccommodationId, requested, item.Name, item.Address)).ToList(),
                "attraction" => (await Attractions.SearchAsync(query, lifetime.Token)).Select(item => new TripPlaceChoice(item.AttractionId, requested, item.Name, item.Address)).ToList(),
                _ => (await Transport.SearchPointsAsync(query, "transport_stop", lifetime.Token)).Select(item => new TripPlaceChoice(item.RoutePointId, requested, item.Name, item.Address)).ToList(),
            };
            // the selector moved on while the request was in flight, so these results are for the wrong kind
            if (requested != type) return;
            results = found;
            searched = true;
        }
        catch (ApiError error)
        {
            results = [];
            failure = error.Message;
            Logger.LogWarning("Searching {Type} places failed as {Kind} (status {Status})", type, error.Kind, error.Status);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // the view closed while the search was running
        }
        finally
        {
            busy = false;
        }
    }

    async Task AddedAsync(AccommodationDto saved)
    {
        adding = false;
        await Choose(new TripPlaceChoice(saved.AccommodationId, "accommodation", saved.Name, saved.Address));
    }

    async Task Choose(TripPlaceChoice choice)
    {
        changing = false;
        results = [];
        searched = false;
        await ValueChanged.InvokeAsync(choice);
    }

    /** Cancels a search that is still running when the view closes. */
    public void Dispose()
    {
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
