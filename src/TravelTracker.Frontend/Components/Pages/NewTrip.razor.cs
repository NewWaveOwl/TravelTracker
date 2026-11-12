using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Components.Pages;

/** Coordinates the two editing views, review and the authenticated Trips contract. */
public partial class NewTrip : IAsyncDisposable
{
    const int UndoLimit = 10;
    static readonly (string Code, string Label)[] Markers =
        [("transport_stop", "Stop"), ("attraction", "Attraction"), ("accommodation", "Accommodation")];

    static readonly (string Code, string Label)[] Modes =
        [("pedestrian", "Walk"), ("bike", "Bike"), ("car", "Car"), ("bus", "Bus"), ("train", "Train"), ("plane", "Plane")];

    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] TripPlacesClient PlaceCatalogue { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Inject] IJSRuntime JavaScript { get; set; } = default!;
    [Inject] IServiceProvider Services { get; set; } = default!;
    [Inject] ILogger<NewTrip> Logger { get; set; } = default!;
    [Parameter] public Guid? TripId { get; set; }
    [CascadingParameter] Microsoft.AspNetCore.Components.RouteData? ActiveRoute { get; set; }

    readonly CancellationTokenSource closed = new();
    readonly List<Action> undoHistory = [];
    readonly Dictionary<TripPlaceDto, HashSet<Guid>> pendingPlaceCreates = [];
    readonly HashSet<string> pendingModeCreates = new(StringComparer.OrdinalIgnoreCase);
    ElementReference plannerElement;
    Task<IJSObjectReference?>? undoShortcut;
    TripPlannerDraft draft = new();
    IReadOnlyList<TripPlaceDto> places = [];
    IReadOnlyList<string> errors = [];
    int selected = -1;
    enum MapTool { Markers, Transport }

    bool showMap;
    bool browseAccommodations;
    bool browseSaved;
    bool stopsStep;
    bool toolsOpen = true;
    bool savedReview;
    MapTool? mapTool;
    (string Code, string Label)? pendingMode;
    int? connectionFrom;
    int insertAfter;
    bool review;
    bool busy;
    bool openingRoute;
    string editorPath = "new-trip";
    bool savingCatalogue;
    bool dirty;
    bool searched;
    bool placing;
    TripPlannerDraft.Stop? editingMarker;
    bool editingStopDetails;
    int markerNumber;
    bool saveFailed;
    string? observedPath;
    Guid? routeId;
    int routeVersion;
    Guid? pendingOpen;
    string? notice;
    string? browserDraftNotice;
    (string Prefix, BrowserDraft Snapshot)? newBrowserDraft;
    readonly HashSet<TripPlaceDto> restoredPlaces = [];
    string? markerType;
    string searchValue = "";
    string placeName = "";
    string placeAddress = "";
    string openValue = "";
    decimal? latitude;
    decimal? longitude;

    TripPlannerDraft.Leg? CurrentLeg => selected > 0 && selected < draft.Stops.Count ? draft.LegTo(selected) : null;
    Guid? SelectedAccommodationId => selected >= 0 && selected < draft.Stops.Count &&
        draft.Stops[selected].Place is { Type: "accommodation" } place ? place.PlaceId : null;
    (decimal Lat, decimal Lon)? PickedCoordinates => latitude is { } lat && longitude is { } lon ? (lat, lon) : null;

    static string Pressed(bool active) => active ? "true" : "false";

    bool MapVisible => showMap || review;
    string TripTitle => string.IsNullOrWhiteSpace(draft.Name) ? "Untitled trip" : draft.Name;
    bool CanUndo => !busy && undoHistory.Count > 0;
    bool HasUnsavedWork => dirty || editingMarker is { } marker &&
        (placeName != marker.Place.Name || placeAddress != marker.Place.Address || latitude != marker.Place.Lat || longitude != marker.Place.Lon);
    bool HasPendingCatalogueEntries => draft.Stops.Any(stop => stop.Place.PlaceId == Guid.Empty) ||
        Enumerable.Range(1, Math.Max(0, draft.Stops.Count - 1)).Select(draft.LegTo)
            .Any(leg => leg.Mode?.TransportModeId == Guid.Empty);
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

    /**
     * Opens a connection and moves focus to the duration field for keyboard activation.
     * @param activation The destination index and whether the activation came from the keyboard.
     */
    void EditConnection((int Destination, bool FromKeyboard) activation)
    {
        var (destination, fromKeyboard) = activation;
        if (busy || review || destination <= 0 || destination >= draft.Stops.Count)
            return;

        ShowStops();
        selected = destination;
        focusDuration = fromKeyboard;
    }

    ElementReference durationHoursInput;
    ElementReference stopNameInput;
    ElementReference toolsToggle;
    bool focusDuration;
    bool focusStopDetails;
    bool focusToolsToggle;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            undoShortcut = RegisterUndoShortcutAsync();
        if (focusDuration)
        {
            focusDuration = false;
            await durationHoursInput.FocusAsync();
        }
        if (focusStopDetails)
        {
            focusStopDetails = false;
            await stopNameInput.FocusAsync();
        }
        if (focusToolsToggle)
        {
            focusToolsToggle = false;
            await toolsToggle.FocusAsync();
        }
        if (firstRender)
            await undoShortcut!;
    }

    /** @return The browser registration used to release the trip shortcut on navigation. */
    async Task<IJSObjectReference?> RegisterUndoShortcutAsync()
    {
        try
        {
            await using var module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/NewTrip.razor.js");
            return module is null ? null : await module.InvokeAsync<IJSObjectReference>("watchUndo", plannerElement);
        }
        catch (JSDisconnectedException) { return null; }
        catch (JSException) { return null; }
    }

    static readonly string[] LocalDateTimeFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF"];

    static string DateTimeValue(DateTime? value) =>
        value?.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture) ?? "";

    static string OffsetValue(TimeSpan offset) => offset.ToString("c", CultureInfo.InvariantCulture);

    static string OffsetLabel(TimeSpan offset) =>
        $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture)}";

    /**
     * Records which occurrence of a repeated time the traveller chose.
     * @param change Selected option value from the offset picker.
     * @param time Local time associated with this picker.
     * @param options Valid offsets for this local time.
     * @param previous Existing choice for undo and change detection.
     * @param apply Updates the associated draft field.
     */
    void SetOffset(ChangeEventArgs change, DateTime time, TimeSpan[] options,
        TripPlannerDraft.OffsetChoice? previous, Action<TripPlannerDraft.OffsetChoice?> apply)
    {
        if (TimeSpan.TryParseExact(change.Value?.ToString(), "c", CultureInfo.InvariantCulture, out var offset) &&
            options.Contains(offset))
            EditTripValue(previous, new TripPlannerDraft.OffsetChoice(time, draft.TimeZone.Trim(), offset), apply);
    }

    void SetDateTime(ChangeEventArgs change, DateTime? previous, Action<DateTime?> apply)
    {
        var input = change.Value?.ToString();
        if (string.IsNullOrEmpty(input))
            EditTripValue(previous, null, apply);
        else if (DateTime.TryParseExact(input, LocalDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            EditTripValue(previous, new DateTime(date.Ticks - date.Ticks % TimeSpan.TicksPerMinute, date.Kind), apply);
        else
        {
            errors = ["Enter a valid date and time."];
            saveFailed = false;
            return;
        }
    }

    long? DisplayedDuration(TripPlannerDraft.Leg connection) =>
        draft.DurationFor(connection) is { } duration ? (long)Math.Ceiling(duration.TotalMinutes) : null;

    /** Updates one duration field and keeps the other part of the connection's elapsed time. */
    void SetDurationPart(TripPlannerDraft.Leg connection, ChangeEventArgs change, bool hours)
    {
        var input = change.Value?.ToString();
        var part = 0;
        if (!string.IsNullOrEmpty(input) &&
            (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out part) || (!hours && part >= 60)))
        {
            errors = [hours ? "Enter whole hours, zero or greater." : "Enter whole minutes from 0 to 59."];
            saveFailed = false;
            return;
        }

        var current = DisplayedDuration(connection) ?? 0;
        var minutes = hours ? part * 60L + current % 60 : current / 60 * 60 + part;
        if (minutes > int.MaxValue)
        {
            errors = ["Enter a shorter duration for this connection."];
            saveFailed = false;
            return;
        }

        EditTripValue(connection.DurationMinutes, minutes == 0 ? null : (int)minutes,
            value => connection.DurationMinutes = value);
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
        var path = PlannerPath(Navigation.Uri);
        if (path == observedPath)
            return;

        var previousPath = observedPath;
        observedPath = path;
        routeVersion++;
        browserDraftNotice = null;
        busy = false;
        openingRoute = false;
        showMap = path.Split('/')[0].Equals("map", StringComparison.OrdinalIgnoreCase);
        if (path.Split('/')[0] is "trip" or "new-trip")
            editorPath = path.Split('/')[0];
        ShowBasics();

        // Map and New Trip share this component, even when the router reuses the same parameters.
        var tripId = ActiveRoute is null ? TripId : ActiveRoute.RouteValues.GetValueOrDefault(nameof(TripId)) as Guid?;
        if (tripId is { } id)
        {
            if (id != routeId || draft.Id != id)
                await OpenTripAsync(id, restoreRouteOnFailure: true);
        }
        else if (previousPath is not null && draft.Id is not null)
        {
            ResetPlannerDraft();
        }
    }

    /** Creates an empty trip only after the traveller chooses to discard unsaved changes. */
    async Task CreateNewAsync()
    {
        if (busy) return;
        var version = routeVersion;
        if (HasUnsavedWork && !await JavaScript.InvokeAsync<bool>("confirm", closed.Token,
                "Discard your unsaved trip changes and create a new trip?"))
            return;
        if (version != routeVersion || closed.IsCancellationRequested) return;
        routeVersion++;
        ResetPlannerDraft();
        NavigateToPlanner(showMap, null);
    }

    /** Replaces all trip-specific buffers after an approved new-trip action or navigation. */
    void ResetPlannerDraft()
    {
        browseSaved = false;
        draft = new();
        ResetBrowserDraftTracking();
        undoHistory.Clear();
        pendingPlaceCreates.Clear();
        pendingModeCreates.Clear();
        routeId = null;
        selected = -1;
        pendingOpen = null;
        markerNumber = 0;
        openValue = "";
        dirty = false;
        notice = null;
        errors = [];
        saveFailed = false;
        ShowBasics();
    }

    /**
     * Gets the planner route without a query, fragment or trailing slash.
     * @param location The target URL or a relative route.
     * @return The normalized route relative to the app base.
     */
    string PlannerPath(string location) =>
        Navigation.ToBaseRelativePath(Navigation.ToAbsoluteUri(location).AbsoluteUri).Split('?', '#')[0].TrimEnd('/');

    sealed record BrowserDraft(TripPlannerDraft.BrowserSnapshot Map, int MarkerNumber,
        PendingPlace[] PendingPlaces, string[] PendingModes);

    sealed record PendingPlace(TripPlaceDto Place, Guid[] ExistingIds);

    static string BrowserDraftKey(string prefix, Guid? id) => $"{prefix}-{id?.ToString("D") ?? "new"}";

    static bool IsBrowserDraftFailure(Exception error) =>
        error is JSException or CryptographicException or JsonException or InvalidOperationException or InvalidDataException;

    static string? AccountDraftPrefix(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true || user.FindFirst("sub") is not { } subject)
            return null;

        var identity = JsonSerializer.Serialize(new[] { user.FindFirst("iss")?.Value ?? subject.Issuer, subject.Value });
        var owner = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"traveltracker-map-draft-v1-{owner}";
    }

    void ResetBrowserDraftTracking()
    {
        newBrowserDraft = null;
        restoredPlaces.Clear();
    }

    /**
     * Saves or restores a protected browser copy of the current map.
     * @param restore Whether to load the browser copy instead of saving it.
     */
    async Task UseBrowserDraftAsync(bool restore)
    {
        if (busy)
            return;

        var version = routeVersion;
        var token = closed.Token;
        string? key = null;
        busy = true;
        browserDraftNotice = null;
        try
        {
            var auth = await Services.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
            var user = auth.User;
            var accountPrefix = AccountDraftPrefix(user);
            if (user.Identity?.IsAuthenticated == true && accountPrefix is null)
            {
                browserDraftNotice = "Sign in again to use your browser draft.";
                return;
            }
            key = BrowserDraftKey(accountPrefix ?? "traveltracker-map-draft-v1-anonymous", draft.Id);
            ProtectedBrowserStorage storage = accountPrefix is null
                ? Services.GetRequiredService<ProtectedSessionStorage>()
                : Services.GetRequiredService<ProtectedLocalStorage>();
            if (version != routeVersion || token.IsCancellationRequested)
                return;

            if (!restore)
            {
                var snapshot = new BrowserDraft(draft.CaptureBrowserSnapshot(), markerNumber,
                    pendingPlaceCreates.Select(pair => new PendingPlace(pair.Key, pair.Value.ToArray())).ToArray(),
                    pendingModeCreates.ToArray());
                await storage.SetAsync(key, snapshot);
                if (version == routeVersion && !token.IsCancellationRequested)
                {
                    if (snapshot.Map.Id is null)
                        newBrowserDraft = accountPrefix is null ? null : (accountPrefix, snapshot);
                    browserDraftNotice = "Map draft saved in this browser.";
                }
                return;
            }

            var result = await storage.GetAsync<BrowserDraft>(key);
            if (version != routeVersion || token.IsCancellationRequested)
                return;
            if (!result.Success || result.Value is not { } saved)
            {
                browserDraftNotice = "There is no saved map draft in this browser.";
                return;
            }
            if (dirty && !await JavaScript.InvokeAsync<bool>("confirm", token,
                    "Replace your unsaved map edits with the browser draft?"))
                return;
            if (version != routeVersion || token.IsCancellationRequested)
                return;

            if (saved.Map is null || saved.Map.Id != draft.Id || saved.PendingPlaces is null || saved.PendingModes is null)
                throw new InvalidDataException("The browser draft does not match this trip or is missing catalogue data.");
            var restored = TripPlannerDraft.FromBrowserSnapshot(saved.Map);
            var pendingPlaces = new Dictionary<TripPlaceDto, HashSet<Guid>>();
            foreach (var place in saved.PendingPlaces)
                if (place?.Place is null || place.ExistingIds is null ||
                    !pendingPlaces.TryAdd(place.Place, place.ExistingIds.ToHashSet()))
                    throw new InvalidDataException("The browser draft contains invalid pending catalogue places.");
            if (saved.PendingModes.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException("The browser draft contains invalid pending transport modes.");

            draft = restored;
            ResetBrowserDraftTracking();
            if (draft.Id is null && accountPrefix is not null)
                newBrowserDraft = (accountPrefix, saved);
            restoredPlaces.UnionWith(draft.Stops.Where(stop => stop.Place.PlaceId == Guid.Empty)
                .Select(stop => PlaceDetails(stop.Place)));
            markerNumber = saved.MarkerNumber;
            selected = draft.Stops.Count > 0 ? 0 : -1;
            pendingPlaceCreates.Clear();
            foreach (var place in pendingPlaces)
                pendingPlaceCreates.Add(place.Key, place.Value);
            pendingModeCreates.Clear();
            pendingModeCreates.UnionWith(saved.PendingModes);
            undoHistory.Clear();
            ShowBasics();
            MarkEdited();
            browserDraftNotice = "Map draft restored from this browser.";
        }
        catch (Exception error) when (IsBrowserDraftFailure(error))
        {
            Logger.LogWarning(error, "Could not {Operation} browser map draft {DraftKey}.", restore ? "restore" : "save", key);
            if (version == routeVersion)
                browserDraftNotice = restore ? "The browser draft could not be restored." : "The browser draft could not be saved.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (version == routeVersion)
                busy = false;
        }
    }

    /**
     * Confirms navigation that would abandon the current draft.
     * @param context The target location and cancellation for the navigation attempt.
     */
    async Task ConfirmDraftNavigationAsync(LocationChangingContext context)
    {
        if (!HasUnsavedWork)
            return;

        var path = PlannerPath(context.TargetLocation);
        var segments = path.Split('/');
        var planner = segments[0].Equals("map", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("new-trip", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("trip", StringComparison.OrdinalIgnoreCase);
        var sameDraft = planner && (segments.Length == 1 && draft.Id is null ||
            segments.Length == 2 && Guid.TryParse(segments[1], out var id) && id == draft.Id);
        if (sameDraft)
            return;

        try
        {
            var discard = await JavaScript.InvokeAsync<bool>("confirm", context.CancellationToken,
                "You have unsaved trip changes. Leave this page and discard them?");
            if (!discard)
                context.PreventNavigation();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
        catch (JSDisconnectedException) { context.PreventNavigation(); }
    }

    /** Cancels outstanding calls and unregisters the shortcut when the editor closes. */
    public async ValueTask DisposeAsync()
    {
        closed.Cancel();
        closed.Dispose();
        if (undoShortcut is null) return;
        try
        {
            await using var registration = await undoShortcut;
            if (registration is not null)
                await registration.InvokeVoidAsync("disconnect");
        }
        catch (JSDisconnectedException) { }
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
        toolsOpen = true;
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
        toolsOpen = true;
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

    /** Slides the editing panel without resetting its selection, buffers or connections. */
    void ToggleTools()
    {
        toolsOpen = !toolsOpen;
        focusToolsToggle = !toolsOpen;
    }

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
        savedReview = false;
        notice = null;
        browserDraftNotice = null;
        errors = [];
        saveFailed = false;
    }

    /** Records the draft and its selection before a single user edit. */
    void RememberEdit()
    {
        var restoreDraft = draft.CaptureUndo();
        var state = (selected, insertAfter, dirty, markerNumber);
        if (undoHistory.Count == UndoLimit)
            undoHistory.RemoveAt(0);
        undoHistory.Add(() =>
        {
            restoreDraft();
            (selected, insertAfter, dirty, markerNumber) = state;
        });
    }

    /** Restores the last draft edit without leaving the current planner view. */
    void Undo()
    {
        if (!CanUndo) return;

        var restore = undoHistory[^1];
        undoHistory.RemoveAt(undoHistory.Count - 1);
        restore();
        CancelConnection();
        if (editingMarker is { } marker)
        {
            if (draft.Stops.Contains(marker))
            {
                var place = marker.Place;
                (placeName, placeAddress, latitude, longitude) = (place.Name, place.Address, place.Lat, place.Lon);
            }
            else
                ClearMarkerDetails();
        }

        notice = "Last change undone.";
        errors = review ? draft.Build().Errors : [];
        saveFailed = false;
    }

    /**
     * Records and applies a changed field value.
     * @param previous The value before editing.
     * @param next The proposed value.
     * @param apply Assigns the new value.
     */
    void EditTripValue<T>(T previous, T next, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(previous, next)) return;
        RememberEdit();
        apply(next);
        MarkEdited();
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
        {
            if (showMap && !review)
                EditSelectedStop();
            return;
        }

        if (connectionFrom is not { } from)
        {
            connectionFrom = index;
            return;
        }

        if (index == from)
            return;

        RememberEdit();
        selected = draft.JoinStops(from, index);
        var leg = draft.LegTo(selected);
        if (leg.Mode?.Code != mode.Code)
            leg.Mode = new(Guid.Empty, mode.Code, mode.Label);
        MarkEdited();
        CancelConnection();
    }

    void MoveStop(int index, int offset)
    {
        RememberEdit();
        CancelConnection();
        draft.Move(index, index + offset);
        selected = index + offset;
        MarkEdited();
    }

    /** Moves a pin in Map View, returning from review to editing when necessary. */
    void MoveMarker((int Index, decimal Lat, decimal Lon) move)
    {
        if (busy || (review && !showMap) || move.Index < 0 || move.Index >= draft.Stops.Count)
            return;

        var stop = draft.Stops[move.Index];
        var previous = stop.Place;
        if (previous.Lat == move.Lat && previous.Lon == move.Lon)
            return;

        RememberEdit();
        if (review)
            ShowStops();
        var updated = previous with { PlaceId = Guid.Empty, Lat = move.Lat, Lon = move.Lon, Address = "" };
        draft.UpdatePlace(stop, updated);
        selected = move.Index;
        CancelConnection();
        if (ReferenceEquals(editingMarker?.Place, updated))
            (latitude, longitude, placeAddress) = (move.Lat, move.Lon, "");
        MarkEdited();
    }

    void RemoveStop(int index)
    {
        if (review || busy || index < 0 || index >= draft.Stops.Count)
            return;

        RememberEdit();
        CancelConnection();
        if (editingMarker == draft.Stops[index])
            ClearMarkerDetails();
        draft.Remove(index);
        selected = Math.Min(index, draft.Stops.Count - 1);
        insertAfter = selected;
        MarkEdited();
    }

    void AddStop(TripPlaceDto place, int? after = null, bool returnToStart = false)
    {
        RememberEdit();
        selected = returnToStart ? draft.AddReturnToStart() : draft.Add(place, after);
        insertAfter = selected;
        placing = false;
        editingMarker = null;
        MarkEdited();
    }

    /**
     * Adds a confirmed catalogue accommodation to the shared itinerary, or selects its existing stop.
     * @param accommodation The selected public record, or null when catalogue selection is cleared.
     */
    void SelectAccommodation(AccommodationDto? accommodation)
    {
        if (accommodation is null || busy || MapVisible)
            return;

        for (var index = 0; index < draft.Stops.Count; index++)
        {
            var place = draft.Stops[index].Place;
            if (place.Type == "accommodation" && place.PlaceId == accommodation.AccommodationId)
            {
                SelectStop(index);
                return;
            }
        }

        AddStop(new(accommodation.AccommodationId, "accommodation", accommodation.Name,
            accommodation.Address, accommodation.Lat, accommodation.Lon));
    }

    void AddReturnStop() => AddStop(draft.Stops[0].Place, returnToStart: true);

    void SelectMarker(string type)
    {
        if (showMap && (editingStopDetails || !placing || type != markerType))
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
        editingStopDetails = false;
        placeName = "";
        placeAddress = "";
        latitude = null;
        longitude = null;
    }

    /** Opens an isolated edit buffer for the selected stop without moving the map. */
    void EditSelectedStop()
    {
        toolsOpen = true;
        var marker = draft.Stops[selected];
        editingMarker = marker;
        editingStopDetails = true;
        focusStopDetails = true;
        placing = true;
        stopsStep = true;
        mapTool = MapTool.Markers;
        markerType = marker.Place.Type;
        (placeName, placeAddress, latitude, longitude) =
            (marker.Place.Name, marker.Place.Address, marker.Place.Lat, marker.Place.Lon);
    }

    /** Discards unconfirmed marker fields while preserving the itinerary's original place. */
    void CancelMarkerDetails()
    {
        placing = false;
        ClearMarkerDetails();
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

        var useCustomName = editingMarker is null && !string.IsNullOrWhiteSpace(placeName);
        var name = useCustomName ? placeName.Trim() : $"{MarkerLabel(type)} {markerNumber + 1}";
        var address = editingMarker is null ? placeAddress.Trim() : "";
        AddStop(new(Guid.Empty, type, name, address, position.Lat, position.Lon), insertAfter);
        if (!useCustomName)
            markerNumber++;
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
            var place = marker.Place with { Name = placeName.Trim(), Address = placeAddress.Trim(), Lat = position.Lat, Lon = position.Lon };
            if (place != marker.Place)
                place = place with { PlaceId = Guid.Empty };
            EditTripValue(marker.Place, place, value => draft.UpdatePlace(marker, value));
            return;
        }
        AddStop(new(Guid.Empty, type, placeName.Trim(), placeAddress.Trim(), position.Lat, position.Lon), showMap ? insertAfter : null);
    }

    void SelectMode((string Code, string Label) option)
    {
        if (CurrentLeg is { } leg && leg.Mode?.Code != option.Code)
        {
            EditTripValue(leg.Mode, new TripDto.Mode(Guid.Empty, option.Code, option.Label), value => leg.Mode = value);
        }
    }

    async Task RequestOpenAsync()
    {
        var input = openValue.Trim().Split('?', '#')[0].TrimEnd('/');
        var lastSegment = input[(input.LastIndexOf('/') + 1)..];
        if (!Guid.TryParse(lastSegment, out var id) || id == Guid.Empty)
        {
            errors = ["Enter a saved trip link or its trip ID."];
            saveFailed = false;
            return;
        }
        if (HasUnsavedWork)
            pendingOpen = id;
        else
            await OpenTripAsync(id);
    }

    /**
     * Loads a saved trip and restores the current draft's link if opening a planner route fails.
     * @param id The saved trip to load.
     * @param restoreRouteOnFailure Whether the URL already points at the requested trip.
     */
    async Task OpenTripAsync(Guid id, bool restoreRouteOnFailure = false)
    {
        var version = routeVersion;
        busy = true;
        openingRoute = restoreRouteOnFailure && id != draft.Id;
        pendingOpen = null;
        errors = [];
        saveFailed = false;
        var token = closed.Token;
        try
        {
            var revision = await Trips.GetRevisionAsync(id, token);
            if (version != routeVersion)
                return;
            var saved = revision.Trip;
            draft = TripPlannerDraft.FromSaved(revision);
            browseSaved = false;
            ResetBrowserDraftTracking();
            undoHistory.Clear();
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
            if (restoreRouteOnFailure)
            {
                routeId = draft.Id;
                NavigateToPlanner(showMap, draft.Id, replace: true);
            }
        }
        catch (ApiError) when (version != routeVersion) { }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally
        {
            if (version == routeVersion)
            {
                busy = false;
                openingRoute = false;
            }
        }
    }

    /** Saves the itinerary using confirmed catalogue references only. */
    async Task SaveAsync()
    {
        if (busy || HasPendingCatalogueEntries)
            return;

        var (request, problems) = draft.Build();
        errors = problems;
        saveFailed = false;
        if (request is null)
            return;

        busy = true;
        var token = closed.Token;
        var version = routeVersion;
        try
        {
            var revision = await Trips.SaveAsync(draft.Id, request, draft.ETag, token);
            if (version != routeVersion)
                return;
            var saved = revision.Trip;
            var checkpoint = draft.Id is null ? newBrowserDraft : null;
            draft = TripPlannerDraft.FromSaved(revision);
            ResetBrowserDraftTracking();
            undoHistory.Clear();
            dirty = false;
            savedReview = true;
            notice = $"Saved {saved.Name}.";
            UpdateLink(saved.TripId);
            if (checkpoint is { } local)
                await CopyBrowserDraftToSavedTripAsync(local.Prefix, local.Snapshot, saved.TripId, revision.ETag, version);
        }
        catch (ApiError error) when (version == routeVersion)
        {
            if (error.Status is 412 or 428)
            {
                errors = ["The saved trip version changed. Your edits are still here. Reopen the trip before saving again."];
                saveFailed = false;
                return;
            }

            errors = error.Status == 403
                ? ["The Trips API denied this save. Check that your sign-in has trips.write and catalogue.read, then sign out and back in after any permission change. You can still save a map draft in this browser."]
                : error.Errors.Count == 0
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

    /** Keeps the local checkpoint reachable from the assigned trip URL after its first server save. */
    async Task CopyBrowserDraftToSavedTripAsync(string prefix, BrowserDraft snapshot, Guid id, string etag, int version)
    {
        var key = BrowserDraftKey(prefix, id);
        try
        {
            var auth = await Services.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
            if (version != routeVersion || closed.IsCancellationRequested || AccountDraftPrefix(auth.User) != prefix)
                return;

            var storage = Services.GetRequiredService<ProtectedLocalStorage>();
            await storage.SetAsync(key, snapshot with { Map = snapshot.Map with { Id = id, ETag = etag } });
        }
        catch (Exception error) when (IsBrowserDraftFailure(error))
        {
            Logger.LogWarning(error, "Could not copy the browser map draft to {DraftKey}.", key);
            if (version == routeVersion)
                browserDraftNotice = "The trip was saved, but the browser draft could not be copied to its new link.";
        }
    }

    /** Confirms catalogue references independently of saving the itinerary. */
    async Task SaveCatalogueEntriesAsync()
    {
        if (busy || savingCatalogue || !HasPendingCatalogueEntries)
            return;

        var (request, problems) = draft.Build();
        errors = problems;
        saveFailed = false;
        if (request is null)
            return;

        busy = true;
        savingCatalogue = true;
        var version = routeVersion;
        var token = closed.Token;
        try
        {
            await ResolveCatalogueAsync(draft, version, token);
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
            savingCatalogue = false;
            if (version == routeVersion)
                busy = false;
        }
    }

    /**
     * Resolves temporary references and retains confirmed IDs for catalogue retries.
     * @param savingDraft The itinerary being prepared.
     * @param version The planner route version at the start of the operation.
     * @param token Cancels pending work.
     */
    async Task ResolveCatalogueAsync(TripPlannerDraft savingDraft, int version, CancellationToken token)
    {
        var pendingPlaces = savingDraft.Stops.Where(stop => stop.Place.PlaceId == Guid.Empty)
            .GroupBy(stop => PlaceDetails(stop.Place));
        foreach (var group in pendingPlaces)
        {
            var place = await ResolvePlaceAsync(group.Key, version, token);
            if (place is null)
                return;
            foreach (var stop in group)
                stop.Place = place;

            // Older snapshots contain temporary IDs for the now-persisted locations.
            if (ReferenceEquals(draft, savingDraft))
                undoHistory.Clear();
            if (version != routeVersion)
                return;
        }

        var pendingModes = Enumerable.Range(1, savingDraft.Stops.Count - 1).Select(savingDraft.LegTo)
            .Where(leg => leg.Mode is { TransportModeId: var id } && id == Guid.Empty).GroupBy(leg => leg.Mode!.Code);
        foreach (var group in pendingModes)
        {
            var mode = await ResolveModeAsync(group.First().Mode!, version, token);
            if (mode is null)
                return;
            foreach (var leg in group)
                leg.Mode = mode;
            if (ReferenceEquals(draft, savingDraft))
                undoHistory.Clear();
            if (version != routeVersion)
                return;
        }
    }

    /**
     * Creates a place once or looks for its confirmed record after an ambiguous response.
     * @param place New location with normalized fields and an empty ID.
     * @param version The planner route version at the start of the operation.
     * @param token Cancels pending work.
     * @return A confirmed location, or null if navigation superseded the lookup.
     */
    async Task<TripPlaceDto?> ResolvePlaceAsync(TripPlaceDto place, int version, CancellationToken token)
    {
        // Search by a single token so normalizing whitespace between words doesn't hide the saved record.
        var searchName = place.Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var entries = await PlaceCatalogue.SearchAsync(place.Type, searchName, token);
        var normalizedName = searchName.Normalize();
        if (version == routeVersion && normalizedName != searchName)
            entries = entries.Concat(await PlaceCatalogue.SearchAsync(place.Type, normalizedName, token)).ToArray();
        if (version != routeVersion)
            return null;

        if (pendingPlaceCreates.TryGetValue(place, out var existingIds))
        {
            var recovered = entries.Where(entry => entry.PlaceId != Guid.Empty && !existingIds.Contains(entry.PlaceId) &&
                MatchesCreatedPlace(place, entry)).DistinctBy(entry => entry.PlaceId).Take(2).ToArray();
            if (recovered.Length != 1)
                throw UnconfirmedCatalogueEntry(place.Name);
            pendingPlaceCreates.Remove(place);
            return recovered[0];
        }

        if (restoredPlaces.Contains(place))
        {
            var matches = entries.Where(entry => entry.PlaceId != Guid.Empty && MatchesCreatedPlace(place, entry))
                .DistinctBy(entry => entry.PlaceId).Take(2).ToArray();
            if (matches.Length > 1)
                throw new ApiError(FailureKind.Validation,
                    $"Several catalogue entries match {place.Name}. Select the intended entry before saving.");
            if (matches.Length == 1)
                return matches[0];
        }

        // An existing identical record cannot confirm the outcome of this POST.
        pendingPlaceCreates.Add(place, entries.Select(entry => entry.PlaceId).ToHashSet());
        try
        {
            var created = await PlaceCatalogue.CreateAsync(place, token);
            if (created.PlaceId == Guid.Empty)
                throw UnconfirmedCatalogueEntry(place.Name);
            pendingPlaceCreates.Remove(place);
            return created;
        }
        catch (ApiError error) when (CatalogueWriteRejected(error))
        {
            pendingPlaceCreates.Remove(place);
            throw;
        }
    }

    /**
     * Looks up a mode by its exact code before creating it or recovering an uncertain creation.
     * @param mode Mode to resolve with its draft code and label.
     * @param version The planner route version at the start of the operation.
     * @param token Cancels pending work.
     * @return A confirmed mode, or null if navigation superseded the lookup.
     */
    async Task<TripDto.Mode?> ResolveModeAsync(TripDto.Mode mode, int version, CancellationToken token)
    {
        var entries = await Transport.SearchTransportAsync("", mode.Code, token);
        if (version != routeVersion)
            return null;

        var saved = entries.FirstOrDefault(entry => entry.TransportModeId != Guid.Empty &&
            string.Equals(entry.Code, mode.Code, StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            pendingModeCreates.Remove(mode.Code);
            return new(saved.TransportModeId, saved.Code, saved.Name);
        }

        if (!pendingModeCreates.Add(mode.Code))
            throw UnconfirmedCatalogueEntry(mode.Name);

        try
        {
            var created = await Transport.CreateTransportAsync(
                new() { ["code"] = mode.Code, ["name"] = mode.Name }, token);
            if (created.TransportModeId == Guid.Empty)
                throw UnconfirmedCatalogueEntry(mode.Name);
            pendingModeCreates.Remove(mode.Code);
            return new(created.TransportModeId, created.Code, created.Name);
        }
        catch (ApiError error) when (CatalogueWriteRejected(error))
        {
            pendingModeCreates.Remove(mode.Code);
            throw;
        }
    }

    static TripPlaceDto PlaceDetails(TripPlaceDto place) =>
        place with { PlaceId = Guid.Empty, Name = place.Name?.Trim() ?? "", Address = place.Address?.Trim() ?? "" };

    /** Matches a catalogue echo after text normalization or coordinate rounding to five decimal places. */
    static bool MatchesCreatedPlace(TripPlaceDto submitted, TripPlaceDto stored) =>
        submitted.Type == stored.Type &&
        string.Equals(NormalizePlaceText(submitted.Name), NormalizePlaceText(stored.Name), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizePlaceText(submitted.Address), NormalizePlaceText(stored.Address), StringComparison.OrdinalIgnoreCase) &&
        Math.Abs(submitted.Lat - stored.Lat) <= 0.000005m && Math.Abs(submitted.Lon - stored.Lon) <= 0.000005m;

    static string NormalizePlaceText(string? value) =>
        string.Join(' ', (value ?? "").Normalize().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    static bool CatalogueWriteRejected(ApiError error) => error.Status is >= 400 and < 500 and not (408 or 409 or 429);

    static ApiError UnconfirmedCatalogueEntry(string name) =>
        new(FailureKind.Unexpected, $"The catalogue save for {name} could not be confirmed. Check the catalogue before retrying.");

    void UpdateLink(Guid id)
    {
        routeId = id;
        NavigateToPlanner(showMap, id, replace: true);
    }

    /** Keeps the URL and navigation links aligned with the displayed editor. */
    void NavigateToPlanner(bool map, Guid? id, bool replace = false)
    {
        var path = map ? "map" : editorPath;
        if (id is { } tripId)
            path += $"/{tripId:D}";

        if (Navigation.ToBaseRelativePath(Navigation.Uri) == path)
            return;

        observedPath = path;
        Navigation.NavigateTo(path, replace: replace);
    }

    static string MarkerLabel(string type) => Markers.First(option => option.Code == type).Label;
    /** Groups adjacent itinerary entries at identical coordinates into a single display row. */
    IEnumerable<(int First, int End)> ItineraryRows()
    {
        for (var first = 0; first < draft.Stops.Count;)
        {
            var place = draft.Stops[first].Place;
            var end = first + 1;
            while (end < draft.Stops.Count && (draft.Stops[end].Place.Lat, draft.Stops[end].Place.Lon) == (place.Lat, place.Lon))
                end++;

            yield return (first, end);
            first = end;
        }
    }

    DateTime? StopTime(int index) =>
        (index > 0 ? draft.ArrivalFor(draft.LegTo(index)) : null)
        ?? (index + 1 < draft.Stops.Count ? draft.LegTo(index + 1).Departure : null);

    static string TimeLabel(DateTime? value) => value?.ToString("MMM d, HH:mm") ?? "Time unset";
}
