using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Attractions;

namespace TravelTracker.Frontend.Components.Pages;

/** Coordinates catalogue reads and draft editing through public HTTP contracts only. */
public partial class Attractions : IDisposable
{
    [Inject] AttractionsClient Client { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;

    IReadOnlyList<AttractionDto> results = [];
    AttractionDto? selected;
    AttractionDto? original;
    AttractionDraft draft = new();
    string query = "";
    string? status;
    ApiError? error;
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    bool signedIn, canWrite, loaded, searching, loadingDetails, editing, saving, pickingLocation, disposed, retrySearch;
    bool focusError, focusDetails, focusLocationEntry;
    ElementReference errorSummary, detailsHeading, locationEntry;
    CancellationTokenSource? request;
    int generation;

    string ErrorHeading => error?.Kind switch
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
        await UpdateAccessAsync(Authentication.GetAuthenticationStateAsync());
        if (signedIn) await SearchAsync();
        loaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusError) { focusError = false; await errorSummary.FocusAsync(); }
        if (focusDetails && selected is not null && !editing) { focusDetails = false; await detailsHeading.FocusAsync(); }
        if (focusLocationEntry && !pickingLocation && editing) { focusLocationEntry = false; await locationEntry.FocusAsync(); }
    }

    /**
     * Cancels obsolete responses when account state changes, clearing previously private catalogue data.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        CancelRequest();
        results = []; selected = original = null; editing = pickingLocation = false;
        saving = searching = loadingDetails = false; status = null;
        await UpdateAccessAsync(state);
        if (signedIn) await SearchAsync();
        StateHasChanged();
    });

    async Task UpdateAccessAsync(Task<AuthenticationState> state)
    {
        var user = (await state).User;
        signedIn = user.Identity?.IsAuthenticated == true && Sessions.IsActive(user) && AccessTokenClaims.ActiveToken(user, Clock.GetUtcNow()) is not null;
        // This hint comes from the server's trusted OAuth grant, never a browser-decoded token.
        canWrite = signedIn && user.FindAll(AccessTokenClaims.GrantedScope).SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Contains("catalogue.write", StringComparer.Ordinal);
    }

    /**
     * Searches by name without replacing an in-progress edit; only the most recent request may publish results.
     * @return Completion of the HTTP read and its user-facing feedback.
     */
    async Task SearchAsync()
    {
        if (saving || !signedIn) return;
        var (current, version) = StartRequest();
        searching = true; loadingDetails = false; ClearError();
        try
        {
            var found = await Client.SearchAsync(query, current.Token);
            if (Current(version)) results = found;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) { ShowError(failure); retrySearch = signedIn; } }
        finally { if (Current(version)) { searching = false; loaded = true; } }
    }

    async Task ClearSearchAsync() { query = ""; await SearchAsync(); }

    async Task ShowDetailsAsync(Guid id)
    {
        if (saving || !signedIn) return;
        var (current, version) = StartRequest();
        loadingDetails = true; searching = false; ClearError(); status = null;
        try
        {
            var found = await Client.GetAsync(id, current.Token);
            if (Current(version)) { selected = found; original = null; editing = false; focusDetails = true; }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) ShowError(failure); }
        finally { if (Current(version)) loadingDetails = false; }
    }

    void BeginCreate()
    {
        if (saving || !canWrite) return;
        CancelRequest(); searching = loadingDetails = false; selected = original = null;
        draft = new(); editing = true; status = null; ClearError();
    }

    void BeginEdit()
    {
        if (saving || !canWrite || selected is null) return;
        original = selected; draft = AttractionDraft.From(selected); editing = true; status = null; ClearError();
    }

    void CancelEdit() { if (saving) return; editing = false; original = null; draft = new(); ClearError(); }
    void OpenLocation() { if (canWrite && !saving) pickingLocation = true; }
    void CloseLocation() { pickingLocation = false; focusLocationEntry = true; }
    void UseLocation((string Lat, string Lon) location)
    {
        if (!canWrite) return;
        draft.Lat = location.Lat; draft.Lon = location.Lon; CloseLocation();
    }

    /**
     * Saves once, retaining the draft on failure and treating refresh failures separately from confirmed writes.
     * @return Completion of the save attempt and subsequent result refresh.
     */
    async Task SaveAsync()
    {
        if (saving || !canWrite) return;
        ClearError(); status = null;
        var validation = draft.Validate();
        if (validation.Count != 0) { errors = validation; error = new ApiError(FailureKind.Validation, "Correct the fields below. Your entries have been kept."); focusError = true; return; }
        var body = original is null ? draft.CreateBody() : draft.PatchBody(original);
        if (original is not null && body.Count == 0) { status = "No saved fields have changed."; return; }
        var (current, version) = StartRequest();
        saving = true; searching = loadingDetails = false;
        try
        {
            var saved = original is null
                ? await Client.CreateAsync(body, current.Token)
                : await Client.UpdateAsync(original.AttractionId, body, current.Token);
            if (!Current(version)) return;
            selected = saved; original = null; editing = false; draft = new(); focusDetails = true;
            status = "Attraction saved. The details below show the confirmed values.";
            try
            {
                var refreshed = await Client.SearchAsync(query, current.Token);
                if (Current(version)) results = refreshed;
            }
            catch (ApiError failure)
            {
                if (!Current(version)) return;
                ShowError(failure); retrySearch = true;
                if (failure.Status != 401) status = "Attraction saved, but search results could not be refreshed. Retry the search; do not submit the attraction again.";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) ShowError(failure); }
        finally { if (Current(version)) saving = false; }
    }

    string? FieldError(string name) => errors.FirstOrDefault(e => string.Equals(e.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { } messages ? string.Join(" ", messages) : null;

    void ShowError(ApiError failure)
    {
        error = failure; errors = failure.Errors; focusError = true; focusDetails = false;
        if (failure.Status == 401) { signedIn = canWrite = false; results = []; selected = original = null; editing = pickingLocation = false; status = null; focusError = false; }
        if (failure.Status == 403) canWrite = false;
    }

    void ClearError() { error = null; errors = new Dictionary<string, string[]>(); retrySearch = false; focusError = false; }
    bool Current(int version) => !disposed && version == generation;
    (CancellationTokenSource Source, int Version) StartRequest() { CancelRequest(); request = new(); return (request, generation); }
    void CancelRequest() { generation++; request?.Cancel(); request?.Dispose(); request = null; }

    /** Unsubscribes account notifications and prevents late HTTP responses from changing a closed view. */
    public void Dispose() { disposed = true; Authentication.AuthenticationStateChanged -= AuthenticationChanged; CancelRequest(); }
}
