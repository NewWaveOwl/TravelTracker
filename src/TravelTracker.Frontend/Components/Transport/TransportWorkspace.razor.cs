using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Transport;

namespace TravelTracker.Frontend.Components.Transport;

/** Coordinates saved public catalogue selections without persisting a trip or inventing schedules. */
public partial class TransportWorkspace : IDisposable
{
    [Inject] TransportClient Client { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Parameter] public EventCallback<IReadOnlyList<TransportSelection>> SelectionChanged { get; set; }

    static readonly string[] Modes = ["car", "bus", "train", "pedestrian", "bike", "plane"];
    static readonly string[] Types = ["accommodation", "attraction", "transport_stop"];
    IReadOnlyList<TransportDto> transports = [];
    IReadOnlyList<RoutePointDto> points = [];
    readonly List<TransportSelection> selections = [];
    TransportDto? selectedTransport;
    RoutePointDto? selectedPoint;
    RoutePointDto? selectedDeparture;
    bool choosingDeparture = true;
    string pointQuery = "", type = "";
    string transportName = "", description = "", newCode = "car";
    string pointName = "", address = "", lat = "", lon = "", sourceId = "", newType = "attraction";
    string? error, status;
    bool signedIn, canWrite, busy, showMap, disposed;
    int? editingIndex;
    readonly CancellationTokenSource lifetime = new();
    CancellationTokenSource? operation;
    string? sessionId;
    IReadOnlyDictionary<string, string[]> fieldErrors = new Dictionary<string, string[]>();

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(await Authentication.GetAuthenticationStateAsync());
        if (signedIn) await LoadAsync();
    }

    /**
     * Clears account-specific draft selections when authentication changes.
     * @param task The new validated identity state.
     */
    void AuthenticationChanged(Task<AuthenticationState> task) => _ = InvokeAsync(async () =>
    {
        operation?.Cancel(); busy = false;
        selections.Clear(); transports = []; points = []; selectedPoint = selectedDeparture = null; selectedTransport = null; choosingDeparture = true;
        transportName = description = pointName = address = lat = lon = sourceId = pointQuery = type = string.Empty;
        fieldErrors = new Dictionary<string, string[]>();
        editingIndex = null; error = status = null;
        await UpdateAccessAsync(await task);
        await SelectionChanged.InvokeAsync(selections.ToArray());
        if (signedIn && !busy) await LoadAsync();
        StateHasChanged();
    });

    Task UpdateAccessAsync(AuthenticationState state)
    {
        sessionId = SessionRegistry.IdFor(state.User);
        signedIn = state.User.Identity?.IsAuthenticated == true && Sessions.IsActive(state.User) && AccessTokenClaims.ActiveToken(state.User, Clock.GetUtcNow()) is not null;
        canWrite = signedIn && state.User.FindAll(AccessTokenClaims.GrantedScope).SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains("catalogue.write", StringComparer.Ordinal);
        return Task.CompletedTask;
    }

    /**
     * Refreshes catalogue filters while retaining pending forms and draft choices on failure.
     * @return Completion of both authenticated collection reads.
     */
    async Task LoadAsync() => await RunAsync(async token =>
    {
        var modes = await Client.SearchTransportAsync("", "", token);
        var locations = await Client.SearchPointsAsync(pointQuery, type, token);
        transports = modes; points = locations;
    });

    async Task SelectTransportAsync(Guid id) => await RunAsync(async token =>
    {
        selectedTransport = await Client.GetTransportAsync(id, token);
        transports = transports.Select(item => item.TransportModeId == id ? selectedTransport : item).ToArray();
        showMap = true;
        choosingDeparture = selectedDeparture is null;
    });
    async Task SelectPointAsync(Guid id)
    {
        var departure = choosingDeparture;
        await RunAsync(async token => ApplyPoint(await Client.GetPointAsync(id, token), departure));
    }

    void ApplyPoint(RoutePointDto point, bool departure)
    {
        if (departure) { selectedDeparture = point; choosingDeparture = false; }
        else selectedPoint = point;
    }

    void ChooseEndpoint(bool departure) { if (!busy) choosingDeparture = departure; }

    /**
     * Rechecks saved public IDs before publishing an ordered draft to the trip editor.
     * @return Completion of selection confirmation and the parent callback.
     */
    async Task AddAsync()
    {
        if (selectedTransport is null || selectedDeparture is null || selectedPoint is null) return;
        var transportId = selectedTransport.TransportModeId; var departureId = selectedDeparture.RoutePointId; var pointId = selectedPoint.RoutePointId;
        await RunAsync(async token =>
        {
            var transport = await Client.GetTransportAsync(transportId, token);
            var departure = await Client.GetPointAsync(departureId, token);
            var destination = await Client.GetPointAsync(pointId, token);
            token.ThrowIfCancellationRequested();
            var entry = new TransportSelection(transport, departure, destination);
            if (editingIndex is { } index) selections[index] = entry; else selections.Add(entry);
            editingIndex = null; selectedTransport = null; selectedPoint = null; selectedDeparture = selections.LastOrDefault()?.Destination; choosingDeparture = selectedDeparture is null;
            await SelectionChanged.InvokeAsync(selections.ToArray());
            status = "Draft updated. Trip saving is not connected yet.";
        });
    }

    void Edit(int index) { if (busy) return; editingIndex = index; selectedTransport = selections[index].Transport; selectedDeparture = selections[index].Departure; selectedPoint = selections[index].Destination; choosingDeparture = false; showMap = true; }
    void NewEntry() { if (busy) return; editingIndex = null; selectedTransport = null; selectedPoint = null; selectedDeparture = selections.LastOrDefault()?.Destination; choosingDeparture = selectedDeparture is null; }
    async Task RemoveAsync(int index) { if (busy) return; selections.RemoveAt(index); NewEntry(); await SelectionChanged.InvokeAsync(selections.ToArray()); }

    async Task CreateTransportAsync()
    {
        if (!canWrite || busy) return;
        if (string.IsNullOrWhiteSpace(transportName) || !Modes.Contains(newCode)) { error = "Enter a transport name and supported mode."; return; }
        await RunAsync(async token =>
        {
            var saved = await Client.CreateTransportAsync(new() { ["name"] = transportName.Trim(), ["code"] = newCode, ["description"] = description }, token);
            selectedTransport = saved; showMap = true; transports = new[] { saved }.Concat(transports.Where(v => v.TransportModeId != saved.TransportModeId)).ToArray();
            status = "Transport entry saved.";
        });
    }

    async Task CreatePointAsync()
    {
        if (!canWrite || busy) return;
        var departure = choosingDeparture;
        if (string.IsNullOrWhiteSpace(pointName) || !Types.Contains(newType) ||
            !decimal.TryParse(lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) || latitude is < -90 or > 90 ||
            !decimal.TryParse(lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude) || longitude is < -180 or > 180 ||
            (!string.IsNullOrWhiteSpace(sourceId) && !Guid.TryParse(sourceId, out _)))
        { error = "Enter a name, supported location type, valid latitude (−90 to 90), longitude (−180 to 180) and optional source UUID."; return; }
        await RunAsync(async token =>
        {
            var saved = await Client.CreatePointAsync(new() { ["name"] = pointName.Trim(), ["type"] = newType, ["address"] = address, ["lat"] = latitude, ["lon"] = longitude, ["sourceId"] = string.IsNullOrWhiteSpace(sourceId) ? null : Guid.Parse(sourceId) }, token);
            ApplyPoint(saved, departure); points = new[] { saved }.Concat(points.Where(v => v.RoutePointId != saved.RoutePointId)).ToArray();
            status = "Location saved.";
        });
    }

    async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (busy || !signedIn || disposed) return;
        busy = true; error = status = null; fieldErrors = new Dictionary<string, string[]>();
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = pending; var capturedSession = sessionId;
        try { await action(pending.Token); }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            if (pending.IsCancellationRequested || capturedSession != sessionId) return;
            fieldErrors = failure.Errors;
            error = failure.Status switch { 404 => "This saved transport or location is no longer available. Refresh the catalogue.", 400 or 422 => "Check the transport or location form fields. Your entries are retained.", _ => failure.Message };
            if (failure.Status == 401)
            {
                signedIn = false; canWrite = false; selections.Clear(); transports = []; points = []; selectedTransport = null; selectedPoint = selectedDeparture = null; choosingDeparture = true;
                transportName = description = pointName = address = lat = lon = sourceId = pointQuery = type = string.Empty;
                await SelectionChanged.InvokeAsync(selections.ToArray());
            }
        }
        finally { if (operation == pending) { operation = null; busy = false; } }
    }

    static string Label(string value) => value switch { "pedestrian" => "Walking", "transport_stop" => "Transport stop", "accommodation" => "Accommodation", "attraction" => "Attraction", "car" => "Car", "bus" => "Bus", "train" => "Train", "bike" => "Bike", "plane" => "Plane", _ => value };

    public void Dispose() { disposed = true; Authentication.AuthenticationStateChanged -= AuthenticationChanged; operation?.Cancel(); lifetime.Cancel(); lifetime.Dispose(); }
}
