using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Components.Trips;

/** Picks one place from its owning catalogue service and reports it by its public identifier. */
public partial class PlaceSelector : IDisposable
{
    [Inject] AccommodationsClient Accommodations { get; set; } = default!;
    [Inject] AttractionsClient Attractions { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Inject] ILogger<PlaceSelector> Logger { get; set; } = default!;

    /** Visible name of the field group. */
    [Parameter, EditorRequired] public string Label { get; set; } = "";

    /** Element id of the chosen-place text, so an error summary can link to it. */
    [Parameter, EditorRequired] public string Id { get; set; } = "";

    /** Kinds the traveller may pick from: accommodation, attraction and transport_stop. */
    [Parameter] public string[] Types { get; set; } = ["accommodation", "attraction", "transport_stop"];

    /** The current choice. */
    [Parameter] public TripPlaceChoice? Value { get; set; }

    /** Raised with the new choice. */
    [Parameter] public EventCallback<TripPlaceChoice> ValueChanged { get; set; }

    /** The field error to show under the group. */
    [Parameter] public string? Error { get; set; }

    string type = "";
    string query = "";
    bool busy, searched, changing;
    string? failure;
    IReadOnlyList<TripPlaceChoice> results = [];
    readonly CancellationTokenSource lifetime = new();

    protected override void OnParametersSet()
    {
        if (type.Length == 0 || !Types.Contains(type)) type = Types[0];
    }

    static string TypeLabel(string kind) => kind switch
    {
        "accommodation" => "Accommodation",
        "attraction" => "Attraction",
        _ => "Transport stop",
    };

    async Task KeyPressedAsync(KeyboardEventArgs key)
    {
        if (key.Key == "Enter") await SearchAsync();
    }

    async Task SearchAsync()
    {
        if (busy) return;
        busy = true;
        failure = null;
        try
        {
            results = type switch
            {
                "accommodation" => (await Accommodations.SearchAsync(query, lifetime.Token)).Select(item => new TripPlaceChoice(item.AccommodationId, type, item.Name, item.Address)).ToList(),
                "attraction" => (await Attractions.SearchAsync(query, lifetime.Token)).Select(item => new TripPlaceChoice(item.AttractionId, type, item.Name, item.Address)).ToList(),
                _ => (await Transport.SearchPointsAsync(query, "transport_stop", lifetime.Token)).Select(item => new TripPlaceChoice(item.RoutePointId, type, item.Name, item.Address)).ToList(),
            };
            searched = true;
        }
        catch (ApiError error)
        {
            results = [];
            failure = error.Message;
            Logger.LogWarning("Searching {Type} places failed as {Kind} (status {Status})", type, error.Kind, error.Status);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { busy = false; }
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
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
