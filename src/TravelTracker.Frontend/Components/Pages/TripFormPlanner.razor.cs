using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Components.Trips;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Features.Settings;
using TravelTracker.Frontend.Features.Trips;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Pages;

/** Assembles a trip in memory, reviews it and saves it in one request, keeping the entries whenever a save fails. */
public sealed partial class TripFormPlanner : IAsyncDisposable
{
    static readonly (int Number, string Title)[] Steps = [(1, "Basics"), (2, "Itinerary"), (3, "Review")];
    static readonly string[] AllTypes = ["accommodation", "attraction", "transport_stop"];

    [Inject] TripsClient Trips { get; set; } = default!;
    [Inject] TransportClient Transport { get; set; } = default!;
    [Inject] UserPreferences Preferences { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] ILogger<TripFormPlanner> Logger { get; set; } = default!;
    [Inject] IJSRuntime JavaScript { get; set; } = default!;

    TripDraft draft = new();
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    IReadOnlyList<TransportDto> modes = [];
    ApiError? saveError;
    string? modeFailure;
    int step = 1;
    bool signedIn, canWrite, loaded, saving, completed, focusHeading, focusAddLeg;
    int generation;
    bool disposed;
    TripDiscardDialog leaveDialog = default!;
    ElementReference stepHeading, addLegButton;
    readonly CancellationTokenSource lifetime = new();
    ElementReference editorElement;
    Task<IJSObjectReference?>? saveShortcut;

    /** true while entries would be lost by leaving, which arms the leave confirmation. */
    bool Dirty => !completed && draft.HasEntries;

    /** summary links only point at controls on the visible step, so other steps' errors stay out of the list. */
    IEnumerable<KeyValuePair<string, string[]>> VisibleErrors => errors.Where(entry => IsItineraryKey(entry.Key) == (step == 2));

    string SaveHeading => saveError?.Kind is FailureKind.Permission or FailureKind.Network or FailureKind.Unavailable ? ErrorHeadings.For(saveError) : "The trip was not saved";

    /** true when a failed create may already exist on the service, so the retry warning applies. */
    bool SaveMayHaveCreated => saveError is not null && CreateRetryWarning.Applies(saveError);

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(await Authentication.GetAuthenticationStateAsync());
        loaded = true;
    }

    /** @return The editor-local shortcut registration released when the form closes. */
    async Task<IJSObjectReference?> RegisterSaveShortcutAsync()
    {
        try
        {
            await using var module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/NewTrip.razor.js");
            return module is null ? null : await module.InvokeAsync<IJSObjectReference>("watchSave", editorElement);
        }
        catch (JSDisconnectedException) { return null; }
        catch (JSException) { return null; }
    }

    /**
     * drops the draft when the signed-in identity changes so one account never inherits another account's entries.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        // an in-flight save from the previous account must not complete into this one
        generation++;
        draft = new();
        errors = new Dictionary<string, string[]>();
        saveError = null;
        // step 1 unmounts the itinerary's PlaceSelectors, which drops their results and searches with them
        step = 1;
        saving = false;
        modes = [];
        modeFailure = null;
        await UpdateAccessAsync(await state);
        StateHasChanged();
    });

    async Task UpdateAccessAsync(AuthenticationState state)
    {
        signedIn = AccessTokenClaims.IsSignedIn(state.User, Sessions, Clock.GetUtcNow());
        canWrite = signedIn && AccessTokenClaims.GrantedScopes(state.User).Contains("trips.write");
        if (!signedIn) return;
        var preferences = await Preferences.LoadAsync(lifetime.Token);
        if (preferences is not null && draft.Name.Length == 0 && !draft.Legs.Any() && SettingsRules.IsKnownTimeZone(preferences.TimeZone)) draft.TimeZone = preferences.TimeZone;
    }

    string[] Messages(string field) => errors.FirstOrDefault(entry => string.Equals(entry.Key, field, StringComparison.OrdinalIgnoreCase)).Value ?? [];

    string? Message(string field) => Messages(field) is { Length: > 0 } messages ? string.Join(" ", messages) : null;

    string? Invalid(string field) => Messages(field).Length > 0 ? "true" : null;

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
            case "start":
            case "end": return "trip-dates";
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

    string FieldLink(string id) => $"{Navigation.ToAbsoluteUri(Navigation.Uri).GetLeftPart(UriPartial.Query)}#{id}";

    [GeneratedRegex(@"^legs\[(\d+)\](?:\.(\w+))?$")]
    private static partial Regex LegField();

    void GoTo(int target)
    {
        errors = new Dictionary<string, string[]>();
        step = target;
        focusHeading = target > 1;
    }

    async Task NextFromBasics()
    {
        saveError = null;
        errors = draft.ValidateBasics();
        if (errors.Count > 0) return;
        if (modes.Count == 0) await LoadModesAsync();
        step = 2;
        focusHeading = true;
    }

    void NextFromItinerary()
    {
        saveError = null;
        errors = draft.ValidateItinerary();
        if (errors.Count != 0) return;
        step = 3;
        focusHeading = true;
    }

    async Task LoadModesAsync()
    {
        var version = generation;
        modeFailure = null;
        try
        {
            var found = await Transport.SearchTransportAsync("", "", lifetime.Token);
            // modes can be private to their owner, so a load that started under the previous account is dropped
            if (version == generation) modes = found;
        }
        catch (ApiError failure)
        {
            if (version == generation) modeFailure = failure.Message;
            Logger.LogWarning("Loading transport modes failed as {Kind} (status {Status})", failure.Kind, failure.Status);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // the view closed while the modes were loading
        }
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
        // the removed leg's controls are gone, so keyboard focus moves to the add control instead of dropping to the body
        focusAddLeg = true;
    }

    void ChooseMode(TripLegDraft leg, string? value)
    {
        var mode = Guid.TryParse(value, out var modeId) ? modes.FirstOrDefault(candidate => candidate.TransportModeId == modeId) : null;
        leg.TransportModeId = mode?.TransportModeId;
        leg.TransportName = mode?.Name ?? "";
    }

    /**
     * saves the whole trip once. the guard is set before the first await, so a repeated click cannot start a second request.
     * @return Completion of the save and its user-facing state.
     */
    async Task SaveAsync()
    {
        if (saving || !canWrite || completed || lifetime.IsCancellationRequested) return;
        saveError = null;
        var local = draft.ValidateBasics();
        foreach (var (key, value) in draft.ValidateItinerary()) local[key] = value;
        if (local.Count > 0)
        {
            ShowErrors(local);
            return;
        }
        errors = new Dictionary<string, string[]>();
        saving = true;
        var version = generation;
        try
        {
            var trip = await Trips.CreateAsync(draft.ToRequest(), lifetime.Token);
            if (version != generation)
            {
                Logger.LogWarning("A trip save finished after the signed-in account changed; the result was not shown");
                return;
            }

            completed = true;
            Logger.LogInformation("A trip was saved with {Legs} legs", trip.Legs.Count);
            Navigation.NavigateTo($"trips/{trip.TripId:D}?saved=true");
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // the view closed while the save was running
        }
        catch (ApiError failure)
        {
            if (version != generation) return;
            saveError = failure;
            Logger.LogWarning("Saving the trip failed as {Kind} (status {Status}); entries are kept", failure.Kind, failure.Status);
            if (failure.Errors.Count > 0) ShowErrors(failure.Errors);
        }
        finally
        {
            if (version == generation) saving = false;
        }
    }

    /**
     * shows the earliest step that still has an error, so basics problems are never hidden behind the itinerary.
     * @param found The errors to show, keyed by public field name.
     */
    void ShowErrors(IReadOnlyDictionary<string, string[]> found)
    {
        errors = found;
        step = found.Keys.All(IsItineraryKey) ? 2 : 1;
    }

    static bool IsItineraryKey(string key) => key.StartsWith("legs", StringComparison.Ordinal);

    /**
     * stops a navigation away from unsaved entries and asks first.
     * @param context The pending navigation.
     */
    async Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        var target = Navigation.ToAbsoluteUri(context.TargetLocation);
        var current = Navigation.ToAbsoluteUri(Navigation.Uri);
        if (!Dirty || target.GetLeftPart(UriPartial.Query) == current.GetLeftPart(UriPartial.Query))
            return;
        try
        {
            if (!await leaveDialog.ConfirmAsync("Leaving will discard the changes you made to this trip.", context.CancellationToken))
                context.PreventNavigation();
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested) { }
        catch (JSException)
        {
            context.PreventNavigation();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            saveShortcut = RegisterSaveShortcutAsync();
            await saveShortcut;
        }
        if (focusHeading)
        {
            focusHeading = false;
            if (step > 1) await stepHeading.FocusAsync();
        }

        if (focusAddLeg)
        {
            focusAddLeg = false;
            await addLegButton.FocusAsync();
        }

    }

    /**
     * Cancels pending requests and releases identity and keyboard listeners when the view closes.
     * @return Completion of browser shortcut cleanup.
     */
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        lifetime.Cancel();
        lifetime.Dispose();
        if (saveShortcut is null) return;
        try
        {
            await using var registration = await saveShortcut;
            if (registration is not null)
                await registration.InvokeVoidAsync("disconnect");
        }
        catch (JSDisconnectedException) { }
    }
}
