using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Trips;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Pages;

/** Assembles a trip in memory, reviews it and saves it in one request, keeping the entries whenever a save fails. */
public sealed partial class TripFormPlanner : IDisposable
{
    static readonly (int Number, string Title)[] Steps = [(1, "Basics"), (2, "Itinerary"), (3, "Review")];
    static readonly string[] AllTypes = ["accommodation", "attraction", "transport_stop"];
    static readonly string[] FirstTypes = ["accommodation"];

    /** time zone names this host knows, offered as suggestions; the service still decides what it accepts. */
    static readonly IReadOnlyList<string> Zones = TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Inject] UserPreferences Preferences { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] ILogger<TripFormPlanner> Logger { get; set; } = default!;

    TripDraft draft = new();
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    IReadOnlyList<TransportDto> modes = [];
    ApiError? saveError;
    string? modeFailure, pendingTarget;
    int step = 1;
    bool signedIn, canWrite, loaded, saving, completed;
    readonly CancellationTokenSource lifetime = new();

    /** true while entries would be lost by leaving, which arms the leave confirmation. */
    bool Dirty => !completed && draft.HasEntries;

    IEnumerable<KeyValuePair<string, string[]>> VisibleErrors => errors;

    string SaveHeading => saveError?.Kind switch
    {
        FailureKind.Validation => "The trip was not saved",
        FailureKind.Permission => "Permission denied",
        FailureKind.Network => "Unable to connect",
        FailureKind.Unavailable => "Service unavailable",
        _ => "The trip was not saved",
    };

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(await Authentication.GetAuthenticationStateAsync());
        loaded = true;
    }

    /**
     * drops the draft when the signed-in identity changes so one account never inherits another account's entries.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        draft = new();
        errors = new Dictionary<string, string[]>();
        saveError = null;
        pendingTarget = null;
        step = 1;
        saving = false;
        await UpdateAccessAsync(await state);
        StateHasChanged();
    });

    async Task UpdateAccessAsync(AuthenticationState state)
    {
        signedIn = state.User.Identity?.IsAuthenticated == true && Sessions.IsActive(state.User) && AccessTokenClaims.ActiveToken(state.User, Clock.GetUtcNow()) is not null;
        canWrite = signedIn && state.User.FindAll(AccessTokenClaims.GrantedScope).SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains("trips.write", StringComparer.Ordinal);
        if (!signedIn) return;
        var preferences = await Preferences.LoadAsync(lifetime.Token);
        if (preferences is not null && draft.Name.Length == 0 && !draft.Legs.Any() && SettingsKnown(preferences.TimeZone)) draft.TimeZone = preferences.TimeZone;
    }

    static bool SettingsKnown(string zone) => Features.Settings.SettingsRules.IsKnownTimeZone(zone);

    string? Message(string field) => errors.TryGetValue(field, out var messages) ? string.Join(" ", messages) : null;

    string[] Messages(string field) => errors.TryGetValue(field, out var messages) ? messages : [];

    string? Invalid(string field) => errors.ContainsKey(field) ? "true" : null;

    /**
     * maps a service or local error key to the control a summary link should land on.
     * @param key An error key such as legs[1].arrival.
     * @return The element id; unknown keys land on the itinerary heading.
     */
    static string FieldId(string key)
    {
        switch (key)
        {
            case "name": return "trip-name";
            case "timeZone": return "trip-timezone";
            case "start": return "trip-start";
            case "end": return "trip-end";
        }
        var match = LegField().Match(key);
        if (!match.Success) return "legs-heading";
        var leg = match.Groups[1].Value;
        return match.Groups[2].Value switch
        {
            "from" => $"leg-{leg}-from",
            "to" => $"leg-{leg}-to",
            "transport" or "transportModeId" => $"leg-{leg}-transport",
            "departure" => $"leg-{leg}-departure",
            "arrival" => $"leg-{leg}-arrival",
            _ => $"leg-{leg}-heading",
        };
    }

    [GeneratedRegex(@"^legs\[(\d+)\](?:\.(\w+))?$")]
    private static partial Regex LegField();

    void GoTo(int target)
    {
        errors = new Dictionary<string, string[]>();
        step = target;
    }

    async Task NextFromBasics()
    {
        saveError = null;
        errors = draft.ValidateBasics();
        if (errors.Count > 0) return;
        if (modes.Count == 0) await LoadModesAsync();
        if (draft.Legs.Count == 0) AddLeg();
        step = 2;
    }

    void NextFromItinerary()
    {
        saveError = null;
        errors = draft.ValidateItinerary();
        if (errors.Count == 0) step = 3;
    }

    async Task LoadModesAsync()
    {
        modeFailure = null;
        try { modes = await Transport.SearchTransportAsync("", "", lifetime.Token); }
        catch (ApiError failure)
        {
            modeFailure = failure.Message;
            Logger.LogWarning("Loading transport modes failed as {Kind} (status {Status})", failure.Kind, failure.Status);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    void AddLeg()
    {
        var leg = new TripLegDraft { From = draft.Legs.LastOrDefault()?.To };
        var preferred = Preferences.Current?.PreferredTransportMode;
        if (modes.FirstOrDefault(mode => mode.Code == preferred) is { } mode) ChooseMode(leg, mode.TransportModeId.ToString());
        draft.Legs.Add(leg);
    }

    void RemoveLeg(int index)
    {
        draft.Legs.RemoveAt(index);
        errors = new Dictionary<string, string[]>();
    }

    void ChooseMode(TripLegDraft leg, string? value)
    {
        var mode = Guid.TryParse(value, out var id) ? modes.FirstOrDefault(candidate => candidate.TransportModeId == id) : null;
        leg.TransportModeId = mode?.TransportModeId;
        leg.TransportName = mode?.Name ?? "";
    }

    /**
     * saves the whole trip once. the guard is set before the first await, so a repeated click cannot start a second request.
     * @return Completion of the save and its user-facing state.
     */
    async Task SaveAsync()
    {
        if (saving || !canWrite) return;
        saveError = null;
        var local = draft.ValidateBasics();
        foreach (var (key, value) in draft.ValidateItinerary()) local[key] = value;
        if (local.Count > 0)
        {
            ShowErrors(local);
            return;
        }
        saving = true;
        try
        {
            var trip = await Trips.CreateAsync(draft.ToRequest(), lifetime.Token);
            completed = true;
            Logger.LogInformation("Trip {TripId} saved with {Legs} legs", trip.TripId, trip.Legs.Count);
            Navigation.NavigateTo($"trips/{trip.TripId:D}?saved=true");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            saveError = failure;
            Logger.LogWarning("Saving the trip failed as {Kind} (status {Status}); entries are kept", failure.Kind, failure.Status);
            if (failure.Errors.Count > 0) ShowErrors(failure.Errors);
        }
        finally { saving = false; }
    }

    void ShowErrors(IReadOnlyDictionary<string, string[]> found)
    {
        errors = found;
        step = found.Keys.Any(key => key.StartsWith("legs", StringComparison.Ordinal)) ? 2 : 1;
    }

    /**
     * stops a navigation away from unsaved entries and asks first.
     * @param context The pending navigation.
     */
    Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        if (!Dirty) return Task.CompletedTask;
        context.PreventNavigation();
        pendingTarget = context.TargetLocation;
        return Task.CompletedTask;
    }

    void KeepEditing() => pendingTarget = null;

    void LeaveWithoutSaving()
    {
        var target = pendingTarget;
        pendingTarget = null;
        completed = true;
        if (target is not null) Navigation.NavigateTo(target);
    }

    /** cancels pending requests and stops listening for identity changes when the view closes. */
    public void Dispose()
    {
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        lifetime.Cancel();
        lifetime.Dispose();
    }
}
