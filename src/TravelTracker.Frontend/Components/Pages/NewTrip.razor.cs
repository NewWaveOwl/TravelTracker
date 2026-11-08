using System.Globalization;
using System.Text.RegularExpressions;
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
    [CascadingParameter] Microsoft.AspNetCore.Components.RouteData? ActiveRoute { get; set; }

    readonly CancellationTokenSource closed = new();
    TripPlannerDraft draft = new();
    IReadOnlyList<TripPlaceDto> places = [];
    IReadOnlyList<string> errors = [];
    int selected = -1;
    enum MapTool { Markers, Transport }

    bool showMap;
    bool stopsStep;
    bool savedReview;
    MapTool? mapTool;
    (string Code, string Label)? pendingMode;
    int? connectionFrom;
    int insertAfter;
    bool review;
    bool busy;
    bool dirty;
    bool searched;
    bool placing;
    TripPlannerDraft.Stop? editingMarker;
    int markerNumber;
    bool saveFailed;
    string? observedPath;
    Guid? routeId;
    int routeVersion;
    Guid? pendingOpen;
    string? notice;
    string? markerType;
    string searchValue = "";
    string placeName = "";
    string placeAddress = "";
    string openValue = "";
    decimal? latitude;
    decimal? longitude;

    TripPlannerDraft.Leg? CurrentLeg => selected > 0 && selected < draft.Stops.Count ? draft.LegTo(selected) : null;
    (decimal Lat, decimal Lon)? PickedCoordinates => latitude is { } lat && longitude is { } lon ? (lat, lon) : null;

    static string Pressed(bool active) => active ? "true" : "false";

    bool MapVisible => showMap || review;
    string ConnectionHint => pendingMode is null ? "Choose a mode, then select a starting stop and a destination."
        : connectionFrom is { } from ? $"From {draft.Stops[from].Place.Name}. Select any other stop as the destination."
        : "Select the starting stop.";

    (string Code, string Label)[] MapLegs => Enumerable.Range(1, Math.Max(0, draft.Stops.Count - 1)).Select(index =>
    {
        var leg = draft.LegTo(index);
        if (leg.Mode is not { } mode) return (string.Empty, string.Empty);
        return (mode.Code, DisplayedDuration(leg) is { } minutes
            ? $"{mode.Name} · {DurationLabel(minutes)}"
            : mode.Name);
    }).ToArray();

    string Icon(string code) => Assets[$"assets/trip-map/{code}.svg"];

    /** Opens the clicked transport connection and focuses its duration field. */
    void EditConnection(int destination)
    {
        if (busy || review || destination <= 0 || destination >= draft.Stops.Count)
            return;

        ShowStops();
        selected = destination;
        focusDuration = true;
    }

    ElementReference durationInput;
    bool focusDuration;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!focusDuration) return;
        focusDuration = false;
        await durationInput.FocusAsync();
    }

    static readonly string[] LocalDateTimeFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"];

    static string DateTimeValue(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) ?? "";

    void SetDateTime(ChangeEventArgs change, Action<DateTime?> apply)
    {
        var input = change.Value?.ToString();
        if (string.IsNullOrEmpty(input))
            apply(null);
        else if (DateTime.TryParseExact(input, LocalDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            apply(date);
        else
        {
            errors = ["Enter a valid date and time."];
            saveFailed = false;
            return;
        }
        MarkEdited();
    }

    long? DisplayedDuration(TripPlannerDraft.Leg connection) =>
        draft.DurationFor(connection) is { } duration ? (long)Math.Ceiling(duration.TotalMinutes) : null;

    void SetDuration(TripPlannerDraft.Leg connection, ChangeEventArgs change)
    {
        var input = change.Value?.ToString();
        if (string.IsNullOrEmpty(input))
            connection.DurationMinutes = null;
        else if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
            connection.DurationMinutes = minutes;
        else
        {
            errors = ["Enter a whole number of minutes greater than zero."];
            saveFailed = false;
            return;
        }
        MarkEdited();
    }

    /** Confirms the active connection's planned times before review. */
    void ApplyConnectionChanges()
    {
        if (CurrentLeg is not { } connection || draft.ArrivalFor(connection) is null ||
            draft.DurationFor(connection) is null)
        {
            errors = ["Enter a departure and a later arrival or a duration for this connection."];
            saveFailed = false;
            return;
        }
        MarkEdited();
    }

    static string DurationLabel(long minutes) => minutes < 60
        ? $"{minutes} min"
        : $"{minutes / 60} h {minutes % 60} min";

    /** Initializes the entry view and loads a trip when a saved-trip link is opened. */
    protected override async Task OnParametersSetAsync()
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].TrimEnd('/');
        if (path == observedPath)
            return;

        var previousPath = observedPath;
        observedPath = path;
        routeVersion++;
        busy = false;
        showMap = path.Split('/')[0].Equals("map", StringComparison.OrdinalIgnoreCase);
        ShowBasics();

        // Map and New Trip share this component, even when the router reuses the same parameters.
        var tripId = ActiveRoute is null ? TripId : ActiveRoute.RouteValues.GetValueOrDefault(nameof(TripId)) as Guid?;
        if (tripId is { } id)
        {
            if (id != routeId || draft.Id != id)
                await OpenTripAsync(id);
        }
        else if (previousPath is not null && draft.Id is not null)
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
        showMap = map;
        ShowBasics();
        NavigateToPlanner(map, draft.Id);
    }

    void ShowBasics()
    {
        placing = false;
        ClearMarkerDetails();
        stopsStep = false;
        review = false;
        savedReview = false;
        mapTool = null;
        CancelConnection();
    }

    void ShowStops()
    {
        stopsStep = true;
        review = false;
        savedReview = false;
        mapTool = MapTool.Transport;
        placing = false;
        ClearMarkerDetails();
        CancelConnection();
    }

    void BackFromReview()
    {
        if (showMap) ShowStops();
        else SwitchView(false);
    }

    void OpenMarkers()
    {
        stopsStep = true;
        mapTool = MapTool.Markers;
        markerType = null;
        placing = false;
        searched = false;
        places = [];
        ClearMarkerDetails();
        insertAfter = selected >= 0 ? selected : draft.Stops.Count - 1;
        CancelConnection();
    }

    void OpenTransport() => ShowStops();

    void CancelConnection()
    {
        pendingMode = null;
        connectionFrom = null;
    }

    void ChooseMode((string Code, string Label) option)
    {
        if (!showMap)
        {
            SelectMode(option);
            return;
        }
        pendingMode = option;
        connectionFrom = null;
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
        savedReview = false;
        CancelConnection();
        errors = draft.Build().Errors;
        saveFailed = false;
    }

    void SelectStop(int index)
    {
        if (busy || index < 0 || index >= draft.Stops.Count)
            return;

        selected = index;
        if (pendingMode is not { } mode)
            return;

        if (connectionFrom is not { } from)
        {
            connectionFrom = index;
            return;
        }

        if (index == from)
            return;

        selected = draft.JoinStops(from, index);
        SelectMode(mode);
        MarkEdited();
        CancelConnection();
    }

    void MoveStop(int index, int offset)
    {
        CancelConnection();
        draft.Move(index, index + offset);
        selected = index + offset;
        MarkEdited();
    }

    /** Changes the draft location without editing a shared catalogue record. */
    void MoveMarker((int Index, decimal Lat, decimal Lon) move)
    {
        if (busy || review || move.Index < 0 || move.Index >= draft.Stops.Count)
            return;

        var stop = draft.Stops[move.Index];
        var previous = stop.Place;
        if (previous.Lat == move.Lat && previous.Lon == move.Lon)
            return;

        var updated = previous with { PlaceId = Guid.Empty, Lat = move.Lat, Lon = move.Lon, Address = "" };
        var lastIndex = draft.Stops.Count - 1;
        if (draft.RoundTrip && lastIndex > 0 && (move.Index == 0 || move.Index == lastIndex))
        {
            var other = draft.Stops[move.Index == 0 ? lastIndex : 0];
            if (other.Place == previous || (previous.PlaceId != Guid.Empty && other.Place.PlaceId == previous.PlaceId))
                other.Place = updated;
        }
        stop.Place = updated;
        selected = move.Index;
        CancelConnection();
        if (editingMarker == stop)
            (latitude, longitude, placeAddress) = (move.Lat, move.Lon, "");
        MarkEdited();
    }

    void RemoveStop(int index)
    {
        if (review || busy || index < 0 || index >= draft.Stops.Count)
            return;

        CancelConnection();
        if (editingMarker == draft.Stops[index])
            ClearMarkerDetails();
        draft.Remove(index);
        selected = Math.Min(index, draft.Stops.Count - 1);
        insertAfter = selected;
        MarkEdited();
    }

    void AddStop(TripPlaceDto place, int? after = null)
    {
        selected = draft.Add(place, after);
        insertAfter = selected;
        placing = false;
        editingMarker = null;
        MarkEdited();
    }

    void AddReturnStop()
    {
        draft.RoundTrip = true;
        AddStop(draft.Stops[0].Place);
    }

    void SelectMarker(string type)
    {
        if (showMap && (!placing || type != markerType))
            BeginPlacement();
        if (type == markerType)
            return;
        markerType = type;
        places = [];
        searched = false;
    }

    async Task SearchAsync()
    {
        if (markerType is not { } type) return;

        busy = true;
        searched = false;
        errors = [];
        saveFailed = false;
        var token = closed.Token;
        var version = routeVersion;
        try
        {
            var results = await PlaceCatalogue.SearchAsync(type, searchValue, token);
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
        ClearMarkerDetails();
        insertAfter = selected >= 0 ? selected : draft.Stops.Count - 1;
        placing = true;
    }

    void ClearMarkerDetails()
    {
        editingMarker = null;
        placeName = "";
        placeAddress = "";
        latitude = null;
        longitude = null;
    }

    /** Starts placement for a palette marker dropped on the map. */
    void PlaceDroppedMarker((string Type, decimal Lat, decimal Lon) marker)
    {
        if (!showMap || review || busy || !Markers.Any(option => option.Code == marker.Type))
            return;

        if (mapTool != MapTool.Markers)
            OpenMarkers();
        SelectMarker(marker.Type);
        PickLocation((marker.Lat, marker.Lon));
    }

    void PickLocation((decimal Lat, decimal Lon) position)
    {
        if (busy || !placing || markerType is not { } type)
            return;

        var name = editingMarker is null && !string.IsNullOrWhiteSpace(placeName)
            ? placeName.Trim() : $"{MarkerLabel(type)} {++markerNumber}";
        var address = editingMarker is null ? placeAddress.Trim() : "";
        AddStop(new(Guid.Empty, type, name, address, position.Lat, position.Lon), insertAfter);
        placing = true;
        editingMarker = draft.Stops[selected];
        placeName = name;
        placeAddress = address;
        (latitude, longitude) = position;
    }

    void AddPickedStop()
    {
        if (markerType is not { } type || string.IsNullOrWhiteSpace(placeName) || PickedCoordinates is not { } position ||
            position.Lat is < -90 or > 90 || position.Lon is < -180 or > 180)
        {
            errors = ["Enter a place name and valid map coordinates."];
            saveFailed = false;
            return;
        }
        if (showMap && editingMarker is { } marker)
        {
            marker.Place = marker.Place with { Name = placeName.Trim(), Address = placeAddress.Trim(), Lat = position.Lat, Lon = position.Lon };
            MarkEdited();
            return;
        }
        AddStop(new(Guid.Empty, type, placeName.Trim(), placeAddress.Trim(), position.Lat, position.Lon), showMap ? insertAfter : null);
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
            savedReview = true;
            notice = $"Saved {saved.Name}.";
            UpdateLink(saved.TripId);
        }
        catch (ApiError error) when (version == routeVersion)
        {
            errors = error.Errors.Count == 0
                ? [error.Message]
                : error.Errors.Values.SelectMany(messages => messages).Where(message => message.Length > 0).DefaultIfEmpty(error.Message)
                    .Select(message => Regex.Replace(message, @"\blegs?\b", match => match.Value.ToLowerInvariant() == "legs"
                        ? "connections" : "connection", RegexOptions.IgnoreCase)).ToArray();
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
        NavigateToPlanner(showMap || review, id, replace: true);
    }

    /** Keeps the URL and navigation links aligned with the displayed editor. */
    void NavigateToPlanner(bool map, Guid? id, bool replace = false)
    {
        var path = map ? "map" : "new-trip";
        if (id is { } tripId)
            path += $"/{tripId:D}";

        if (Navigation.ToBaseRelativePath(Navigation.Uri) == path)
            return;

        observedPath = path;
        Navigation.NavigateTo(path, replace: replace);
    }

    static string MarkerLabel(string type) => Markers.First(option => option.Code == type).Label;
    static string TimeLabel(DateTime? value) => value?.ToString("MMM d, HH:mm") ?? "Time unset";
}
