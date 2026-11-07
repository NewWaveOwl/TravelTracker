using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Settings;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Settings;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Pages;

/** loads and replaces the caller's own preferences through the Settings HTTP contract, guarded by If-Match. */
public partial class Settings : IDisposable
{
    static readonly Dictionary<string, string> FieldIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["theme"] = "settings-theme",
        ["preferredTransportMode"] = "settings-transport",
        ["defaultMapLatitude"] = "settings-latitude",
        ["defaultMapLongitude"] = "settings-longitude",
        ["defaultMapZoom"] = "settings-zoom",
        ["timeZone"] = "settings-timezone",
    };

    [Inject] SettingsClient Client { get; set; } = default!;
    [Inject] UserPreferences Preferences { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] ILogger<Settings> Logger { get; set; } = default!;

    SettingsDraft draft = new();
    SettingsDto? saved;
    string? etag;
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    ApiError? error;
    string? status;
    bool signedIn, canWrite, loaded, loading, saving, conflict, hasValues, focusError, disposed;
    ElementReference errorSummary;
    CancellationTokenSource? request;
    int generation;

    /** time zone names this host knows, offered as suggestions; the service still decides what it accepts. */
    static readonly IReadOnlyList<string> TimeZoneNames = TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    /**
     * zone choices: every zone this host knows, plus the saved value when the host does not know it, so it stays selectable and validation can explain it.
     */
    IReadOnlyList<string> TimeZoneOptions => TimeZoneNames.Contains(draft.TimeZone)
        ? TimeZoneNames
        : TimeZoneNames.Append(draft.TimeZone).OrderBy(zone => zone, StringComparer.Ordinal).ToArray();

    /** saving needs a confirmed revision to echo, write access and no unresolved conflict. */
    bool CanSave => signedIn && canWrite && hasValues && !saving && !loading && !conflict && etag is not null;

    /** field errors whose inputs exist on this form, so summary links always land on a control. */
    IEnumerable<KeyValuePair<string, string[]>> VisibleErrors => errors.Where(entry => FieldIds.ContainsKey(entry.Key));

    string ErrorHeading => conflict ? "Your settings changed" : error?.Kind switch
    {
        FailureKind.Validation => "Check your details",
        FailureKind.Permission => "Permission denied",
        FailureKind.Network => "Unable to connect",
        FailureKind.Unavailable => "Service unavailable",
        _ => "Unable to complete the request",
    };

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(await Authentication.GetAuthenticationStateAsync());
        if (signedIn) await LoadAsync();
        else loaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusError)
        {
            focusError = false;
            await errorSummary.FocusAsync();
        }
    }

    /**
     * drops account-specific values and pending requests when the signed-in identity changes.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        CancelRequest();
        saved = null; etag = null; hasValues = false; draft = new(); status = null; conflict = saving = loading = false;
        ClearError();
        loaded = false;
        await UpdateAccessAsync(await state);
        if (signedIn) await LoadAsync();
        else loaded = true;
        StateHasChanged();
    });

    /**
     * refreshes the sign-in and write hints from the server-owned session, never from a browser-decoded token.
     * @param state The current authentication state.
     * @return Completion once the hints are updated.
     */
    Task UpdateAccessAsync(AuthenticationState state)
    {
        signedIn = state.User.Identity?.IsAuthenticated == true && Sessions.IsActive(state.User) && AccessTokenClaims.ActiveToken(state.User, Clock.GetUtcNow()) is not null;
        canWrite = signedIn && state.User.FindAll(AccessTokenClaims.GrantedScope).SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains("settings.write", StringComparer.Ordinal);
        return Task.CompletedTask;
    }

    /**
     * reads the saved values and replaces the form with them. this is the only path that clears a stale conflict, and only when the read succeeds; a failed reload keeps the conflict so the old etag cannot be saved.
     * @return Completion of the read and its user-facing state.
     */
    async Task LoadAsync()
    {
        var (current, version) = StartRequest();
        var wasConflict = conflict;
        loading = true; status = null; ClearError();
        conflict = wasConflict;
        try
        {
            var revision = await Client.GetAsync(current.Token);
            if (Current(version)) ApplyRevision(revision);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        catch (ApiError failure)
        {
            if (Current(version)) ShowError(failure, "load");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Settings load failed unexpectedly");
            if (Current(version)) ShowError(new ApiError(FailureKind.Unexpected, "Your settings could not be loaded. Try again."), "load");
        }
        finally
        {
            if (Current(version))
            {
                loading = false;
                loaded = true;
            }
        }
    }

    /**
     * validates locally, then replaces the preferences only when the service accepts the revision the form was read from.
     * @return Completion of the save attempt; the form keeps the traveller's values unless the service confirms a save.
     */
    async Task SaveAsync()
    {
        if (!CanSave) return;
        ClearError(); status = null;
        var validation = draft.Validate();
        if (validation.Count != 0)
        {
            errors = validation;
            error = new ApiError(FailureKind.Validation, "Correct the highlighted settings. Your edits are kept.");
            focusError = true;
            return;
        }
        var settings = draft.ToSettings();
        if (settings == saved)
        {
            status = "No changes to save.";
            return;
        }
        var (current, version) = StartRequest();
        saving = true;
        try
        {
            var revision = await Client.SaveAsync(settings, etag!, current.Token);
            if (!Current(version)) return;
            ApplyRevision(revision);
            status = "Settings saved. The fields show the values the service confirmed.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        catch (ApiError failure)
        {
            if (Current(version)) ShowError(failure, "save");
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Settings save failed unexpectedly");
            if (Current(version)) ShowError(new ApiError(FailureKind.Unexpected, "The save could not be completed. Your edits are kept."), "save");
        }
        finally
        {
            if (Current(version)) saving = false;
        }
    }

    /**
     * stores the confirmed values and entity tag, and rebuilds the form from them.
     * @param revision The saved or freshly read values with their entity tag.
     */
    void ApplyRevision(SettingsRevision revision)
    {
        saved = revision.Settings;
        etag = revision.ETag;
        draft = SettingsDraft.From(saved);
        hasValues = true;
        conflict = false;
        errors = new Dictionary<string, string[]>();
        Preferences.Remember(saved);
    }

    /**
     * shows a failure, logs its category without values, and applies sign-out or permission consequences.
     * @param failure The categorized failure.
     * @param operation Which action failed, for the log line.
     */
    void ShowError(ApiError failure, string operation)
    {
        Logger.LogWarning("Settings {Operation} failed as {Kind} (status {Status})", operation, failure.Kind, failure.Status);
        error = failure;
        errors = failure.Errors;
        focusError = true;
        conflict |= failure.Status is 412 or 428;
        if (failure.Status == 401)
        {
            signedIn = canWrite = false;
            Preferences.Forget();
        }
        if (failure.Status == 403) canWrite = false;
    }

    /** clears the error, field messages and conflict state before a new attempt. */
    void ClearError()
    {
        error = null;
        errors = new Dictionary<string, string[]>();
        conflict = false;
        focusError = false;
    }

    /**
     * finds the message for one field, matched case-insensitively.
     * @param name the JSON field name.
     * @return the joined messages, or null when the field is valid.
     */
    string? FieldError(string name) => errors.FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { } messages ? string.Join(" ", messages) : null;

    /**
     * sets aria-invalid for an input that has a message.
     * @param name the JSON field name.
     * @return "true" when the field has an error; otherwise null so the attribute is omitted.
     */
    string? Invalid(string name) => FieldError(name) is null ? null : "true";

    /**
     * describes an input by its hint, and also by its error text while that text exists.
     * @param name the JSON field name.
     * @param errorId the error element id.
     * @param hintId the hint element id, always present.
     * @return the space separated ids to reference.
     */
    string Described(string name, string errorId, string hintId) => FieldError(name) is null ? hintId : $"{hintId} {errorId}";

    /**
     * gives the theme option a readable label.
     * @param theme A theme code the service accepts.
     * @return The display label for the option.
     */
    static string ThemeLabel(string theme) => theme switch
    {
        "light" => "Light",
        "dark" => "Dark",
        _ => "System",
    };

    /**
     * gives the transport option a readable label.
     * @param mode A transport code the service accepts.
     * @return The display label for the option.
     */
    static string ModeLabel(string mode) => mode switch
    {
        "bus" => "Bus",
        "train" => "Train",
        "pedestrian" => "Walking",
        "bike" => "Bicycle",
        "plane" => "Plane",
        _ => "Car",
    };

    /**
     * checks that a response still belongs to the latest request on a view that is still open.
     * @param version the request generation the response was started with.
     * @return true when the response may update the view.
     */
    bool Current(int version) => !disposed && version == generation;
    /**
     * cancels any earlier request and starts a new one.
     * @return the new cancellation source and its generation.
     */
    (CancellationTokenSource Source, int Version) StartRequest()
    {
        CancelRequest();
        request = new();
        return (request, generation);
    }
    /** cancels the pending request and advances the generation so late answers are ignored. */
    void CancelRequest()
    {
        generation++;
        request?.Cancel();
        request?.Dispose();
        request = null;
    }

    /** unsubscribes account notifications and prevents late HTTP responses from changing a closed view. */
    public void Dispose()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        CancelRequest();
    }
}
