using Microsoft.AspNetCore.Components;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Components.Pages;

/** Coordinates the two editing views, review and the authenticated Trips contract. */
public partial class NewTrip : IDisposable
{
    static readonly (string Code, string Label)[] Markers =
        [("transport_stop", "Stop"), ("attraction", "Attraction"), ("accommodation", "Accommodation")];

    static readonly (string Code, string Label)[] Modes =
        [("pedestrian", "Walk"), ("bike", "Bike"), ("car", "Car"), ("bus", "Bus"), ("train", "Train"), ("plane", "Plane")];

    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] TripPlacesClient PlaceCatalogue { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Parameter] public Guid? TripId { get; set; }

    readonly CancellationTokenSource closed = new();
    TripPlannerDraft draft = new();
    IReadOnlyList<TripPlaceDto> places = [];
    IReadOnlyList<string> errors = [];
    int selected = -1;
    bool showMap;
    bool review;
    bool busy;
    bool dirty;
    bool searched;
    bool placing;
    bool saveFailed;
    string? observedPath;
    Guid? routeId;
    int routeVersion;
    Guid? pendingOpen;
    string? notice;
    string markerType = "transport_stop";
    string searchValue = "";
    string placeName = "";
    string placeAddress = "";
    string openValue = "";
    decimal? latitude;
    decimal? longitude;

    TripPlannerDraft.Leg? CurrentLeg => selected > 0 && selected < draft.Stops.Count ? draft.LegTo(selected) : null;
    (decimal Lat, decimal Lon)? PickedCoordinates => latitude is { } lat && longitude is { } lon ? (lat, lon) : null;

    /** Initializes the entry view and loads a trip when a saved-trip link is opened. */
    protected override async Task OnParametersSetAsync()
    {
        var path = new Uri(Navigation.Uri).AbsolutePath.TrimEnd('/');
        if (path == observedPath)
            return;

        var previousPath = observedPath;
        observedPath = path;
        routeVersion++;
        busy = false;
        showMap = path.StartsWith("/map", StringComparison.OrdinalIgnoreCase);
        review = false;

        if (TripId is { } id)
        {
            if (id != routeId || draft.Id != id)
                await OpenTripAsync(id);
        }
        else if (previousPath is not null && (draft.Id is not null || !showMap))
        {
            draft = new();
            routeId = null;
            selected = -1;
            pendingOpen = null;
            placing = false;
            dirty = false;
            notice = null;
            errors = [];
            saveFailed = false;
        }
    }

    /** Cancels outstanding service calls when the editor is closed. */
    public void Dispose()
    {
        closed.Cancel();
        closed.Dispose();
    }

    void SwitchView(bool map)
    {
        review = false;
        showMap = map;
    }

    void MarkEdited()
    {
        dirty = true;
        notice = null;
        errors = [];
        saveFailed = false;
    }

    void ShowReview()
    {
        review = true;
        errors = draft.Build().Errors;
        saveFailed = false;
    }

    void SelectStop(int index)
    {
        if (!busy && index >= 0 && index < draft.Stops.Count)
            selected = index;
    }

    void MoveStop(int index, int offset)
    {
        draft.Move(index, index + offset);
        selected = index + offset;
        MarkEdited();
    }

    void RemoveStop(int index)
    {
        draft.Remove(index);
        selected = Math.Min(index, draft.Stops.Count - 1);
        MarkEdited();
    }

    void AddStop(TripPlaceDto place)
    {
        selected = draft.Add(place);
        placing = false;
        MarkEdited();
    }

    void AddReturnStop()
    {
        draft.RoundTrip = true;
        AddStop(draft.Stops[0].Place);
    }

    void SelectMarker(string type)
    {
        if (type == markerType)
            return;
        markerType = type;
        places = [];
        searched = false;
    }

    async Task SearchAsync()
    {
        busy = true;
        searched = false;
        errors = [];
        saveFailed = false;
        var token = closed.Token;
        var version = routeVersion;
        try
        {
            var results = await PlaceCatalogue.SearchAsync(markerType, searchValue, token);
            if (version == routeVersion)
            {
                places = results;
                searched = true;
            }
        }
        catch (ApiError error) when (version == routeVersion)
        {
            errors = [error.Message];
            saveFailed = true;
        }
        catch (ApiError) when (version != routeVersion) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (version == routeVersion)
                busy = false;
        }
    }

    void BeginPlacement()
    {
        placeName = "";
        placeAddress = "";
        latitude = null;
        longitude = null;
        placing = true;
    }

    void PickLocation((decimal Lat, decimal Lon) position)
    {
        if (!busy && placing)
            (latitude, longitude) = position;
    }

    void AddPickedStop()
    {
        if (string.IsNullOrWhiteSpace(placeName) || PickedCoordinates is not { } position ||
            position.Lat is < -90 or > 90 || position.Lon is < -180 or > 180)
        {
            errors = ["Enter a place name and valid map coordinates."];
            saveFailed = false;
            return;
        }
        AddStop(new(Guid.Empty, markerType, placeName.Trim(), placeAddress.Trim(), position.Lat, position.Lon));
    }

    void SelectMode((string Code, string Label) option)
    {
        if (CurrentLeg is { } leg && leg.Mode?.Code != option.Code)
        {
            leg.Mode = new(Guid.Empty, option.Code, option.Label);
            MarkEdited();
        }
    }

    async Task RequestOpenAsync()
    {
        var input = openValue.Trim().TrimEnd('/');
        var lastSegment = input[(input.LastIndexOf('/') + 1)..];
        if (!Guid.TryParse(lastSegment, out var id) || id == Guid.Empty)
        {
            errors = ["Enter a saved trip link or its trip ID."];
            saveFailed = false;
            return;
        }
        if (dirty)
            pendingOpen = id;
        else
            await OpenTripAsync(id);
    }

    async Task OpenTripAsync(Guid id)
    {
        var version = routeVersion;
        busy = true;
        pendingOpen = null;
        errors = [];
        saveFailed = false;
        var token = closed.Token;
        try
        {
            var saved = await Trips.GetAsync(id, token);
            if (version != routeVersion)
                return;
            draft = TripPlannerDraft.FromSaved(saved);
            selected = draft.Stops.Count > 0 ? 0 : -1;
            placing = false;
            review = false;
            dirty = false;
            notice = $"Opened {saved.Name}.";
            UpdateLink(id);
        }
        catch (ApiError error) when (version == routeVersion)
        {
            errors = [error.Status == 404 ? "That saved trip is unavailable." : error.Message];
            saveFailed = true;
        }
        catch (ApiError) when (version != routeVersion) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (version == routeVersion)
                busy = false;
        }
    }

    async Task SaveAsync()
    {
        if (busy)
            return;

        var (request, problems) = draft.Build();
        errors = problems;
        saveFailed = false;
        if (request is null)
            return;

        busy = true;
        var token = closed.Token;
        var version = routeVersion;
        var savingDraft = draft;
        try
        {
            await ResolveCatalogueAsync(savingDraft, token);
            if (version != routeVersion)
                return;
            var resolved = savingDraft.Build().Request ?? throw new ApiError(FailureKind.Validation, "Check your trip details and try again.");
            var saved = await Trips.SaveAsync(savingDraft.Id, resolved, token);
            if (version != routeVersion)
                return;
            draft = TripPlannerDraft.FromSaved(saved);
            dirty = false;
            notice = $"Saved {saved.Name}.";
            UpdateLink(saved.TripId);
        }
        catch (ApiError error) when (version == routeVersion)
        {
            errors = error.Errors.Count == 0
                ? [error.Message]
                : error.Errors.Values.SelectMany(messages => messages).Where(message => message.Length > 0).DefaultIfEmpty(error.Message).ToArray();
            saveFailed = error.Kind != FailureKind.Validation;
        }
        catch (ApiError) when (version != routeVersion) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (version == routeVersion)
                busy = false;
        }
    }

    async Task ResolveCatalogueAsync(TripPlannerDraft savingDraft, CancellationToken token)
    {
        var pendingPlaces = savingDraft.Stops.Where(stop => stop.Place.PlaceId == Guid.Empty).GroupBy(stop => stop.Place);
        foreach (var group in pendingPlaces)
        {
            var place = await PlaceCatalogue.CreateAsync(group.Key, token);
            foreach (var stop in group)
                stop.Place = place;
        }

        var pendingModes = Enumerable.Range(1, savingDraft.Stops.Count - 1).Select(savingDraft.LegTo)
            .Where(leg => leg.Mode is { TransportModeId: var id } && id == Guid.Empty).GroupBy(leg => leg.Mode!.Code);
        foreach (var group in pendingModes)
        {
            var label = group.First().Mode!.Name;
            var entries = await Transport.SearchTransportAsync("", group.Key, token);
            var mode = entries.FirstOrDefault() ?? await Transport.CreateTransportAsync(
                new() { ["code"] = group.Key, ["name"] = label }, token);
            foreach (var leg in group)
                leg.Mode = new(mode.TransportModeId, mode.Code, mode.Name);
        }
    }

    void UpdateLink(Guid id)
    {
        routeId = id;
        var path = $"{(showMap || review ? "map" : "new-trip")}/{id:D}";
        if (Navigation.ToBaseRelativePath(Navigation.Uri) != path)
        {
            observedPath = $"/{path}";
            Navigation.NavigateTo(path, replace: true);
        }
    }

    static string MarkerLabel(string type) => Markers.First(option => option.Code == type).Label;
    static string TimeLabel(DateTime? value) => value?.ToString("MMM d, HH:mm") ?? "Time unset";
}
