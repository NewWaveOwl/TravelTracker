using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Attractions;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Features.Attractions;

namespace TravelTracker.Frontend.Components.Pages;

/** Coordinates catalogue reads and draft editing through public HTTP contracts only. */
public partial class Attractions : IDisposable
{
    [Inject] AttractionsClient Client { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] ILogger<Attractions> Logger { get; set; } = default!;

    IReadOnlyList<AttractionDto> results = [];
    AttractionDto? selected;
    AttractionDto? original;
    AttractionDraft draft = new();
    string query = "";
    string? status;
    ApiError? error;
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    bool signedIn, canWrite, loaded, searching, loadingDetails, editing, saving, pickingLocation, disposed, retrySearch;
    bool focusError, focusDetails, focusLocationEntry, focusModal, focusCreate;
    ElementReference errorSummary, detailsHeading, locationEntry, modalHeading, modalCancel, createButton;
    CancellationTokenSource? request;
    int generation;

    string ErrorHeading => ErrorHeadings.For(error);

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await UpdateAccessAsync(Authentication.GetAuthenticationStateAsync());
        if (signedIn) await SearchAsync();
        loaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool _firstRender)
    {
        if (focusModal && editing && !pickingLocation) { focusModal = false; await modalHeading.FocusAsync(); }
        if (focusCreate && !editing) { focusCreate = false; await createButton.FocusAsync(); }
        if (focusError)
        {
            focusError = false;
            await errorSummary.FocusAsync();
        }

        if (focusDetails && selected is not null && !editing)
        {
            focusDetails = false;
            await detailsHeading.FocusAsync();
        }

        if (focusLocationEntry && !pickingLocation && editing)
        {
            focusLocationEntry = false;
            await locationEntry.FocusAsync();
        }
    }

    /**
     * Cancels obsolete responses when account state changes, clearing previously private catalogue data.
     * @param state The latest validated authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = AuthenticationChangedAsync(state);

    /**
     * Applies an account change on the render context and logs a failure instead of leaving it unobserved.
     * @param state The latest validated authentication state.
     * @return Completion of the account reset and any search for the new account.
     */
    async Task AuthenticationChangedAsync(Task<AuthenticationState> state)
    {
        if (disposed) return;
        try
        {
            await InvokeAsync(async () =>
            {
                CancelRequest();
                results = []; selected = original = null; editing = pickingLocation = false;
                saving = searching = loadingDetails = false; status = null;
                ClearError();
                query = "";
                await UpdateAccessAsync(state);
                if (signedIn) await SearchAsync();
                StateHasChanged();
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError(exception, "Attractions account change failed");
        }
    }

    async Task UpdateAccessAsync(Task<AuthenticationState> state)
    {
        var user = (await state).User;
        signedIn = AccessTokenClaims.IsSignedIn(user, Sessions, Clock.GetUtcNow());
        // this hint comes from the server's trusted OAuth grant, never a browser-decoded token.
        canWrite = signedIn && AccessTokenClaims.GrantedScopes(user).Contains("catalogue.write");
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
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded by a newer request, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                LogFailure("search", failure);
                HandleReadFailure(failure);
                retrySearch = signedIn && failure.Status != 403;
            }
        }
        finally
        {
            if (Current(version))
            {
                searching = false;
                loaded = true;
            }
        }
    }

    async Task ClearSearchAsync()
    {
        query = "";
        await SearchAsync();
    }

    async Task ShowDetailsAsync(Guid attractionId)
    {
        // an unsaved draft is never replaced silently; the user cancels or saves first
        if (saving || editing || !signedIn) return;
        var (current, version) = StartRequest();
        loadingDetails = true; searching = false; ClearError(); status = null;
        try
        {
            var found = await Client.GetAsync(attractionId, current.Token);
            if (Current(version))
            {
                selected = found;
                original = null;
                editing = false;
                focusDetails = true;
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded by a newer request, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                LogFailure("details", failure);
                HandleReadFailure(failure, attractionId);
            }
        }
        finally
        {
            if (Current(version)) loadingDetails = false;
        }
    }

    void BeginCreate()
    {
        if (saving || editing || !canWrite) return;
        CancelRequest(); searching = loadingDetails = false; selected = original = null;
        draft = new(); editing = true; focusModal = true; status = null; ClearError();
    }

    void BeginEdit()
    {
        if (saving || !canWrite || selected is null) return;
        original = selected; draft = AttractionDraft.From(selected); editing = true; focusModal = true; status = null; ClearError();
    }

    void CancelEdit()
    {
        if (saving) return;
        editing = false;
        focusDetails = selected is not null;
        focusCreate = selected is null;
        original = null;
        draft = new();
        ClearError();
    }

    /**
     * Handles modal dismissal while keeping save requests and keyboard focus safe.
     * @param keyboard The key pressed within the attraction dialog.
     */
    void ModalKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs keyboard)
    {
        if (editing && keyboard.Key == "Escape") CancelEdit();
    }

    async Task FocusModalFirst() => await modalHeading.FocusAsync();
    async Task FocusModalLast() { if (saving) await modalHeading.FocusAsync(); else await modalCancel.FocusAsync(); }

    void OpenLocation()
    {
        if (canWrite && !saving) pickingLocation = true;
    }

    void CloseLocation()
    {
        pickingLocation = false;
        focusLocationEntry = true;
    }

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
        if (validation.Count != 0)
        {
            errors = validation;
            error = new ApiError(FailureKind.Validation, "Correct the fields below. Your entries have been kept.");
            focusError = true;
            return;
        }

        var body = original is null ? draft.CreateBody() : draft.PatchBody(original);
        if (original is not null && body.Count == 0)
        {
            status = "No saved fields have changed.";
            return;
        }

        var (current, version) = StartRequest();
        var creating = original is null;
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
                LogFailure("refresh after save", failure);
                HandleReadFailure(failure);
                retrySearch = signedIn && failure.Status != 403;
                if (failure.Status != 401) status = "Attraction saved, but search results could not be refreshed. Retry the search; do not submit the attraction again.";
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded by a newer request, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                LogFailure("save", failure);
                ShowError(failure);
                if (creating && CreateRetryWarning.Applies(failure)) status = CreateRetryWarning.Message;
            }
        }
        finally
        {
            if (Current(version)) saving = false;
        }
    }

    /** Field ids the editor renders, so a summary link never points at a control that is not on the form. */
    static readonly HashSet<string> RenderedFields = new(StringComparer.OrdinalIgnoreCase)
    { "name", "address", "lat", "lon", "estimatedVisitMinutes", "approximationMinutes", "rating", "description" };

    /** Field errors whose inputs exist on this form, so summary links always land on a control. */
    IEnumerable<KeyValuePair<string, string[]>> VisibleErrors => errors.Where(entry => RenderedFields.Contains(entry.Key));

    string? FieldError(string name) => errors.FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { } messages ? string.Join(" ", messages) : null;

    void ShowError(ApiError failure)
    {
        error = failure; errors = failure.Errors; focusError = true; focusDetails = false;
        if (failure.Status == 401)
        {
            signedIn = canWrite = false;
            results = [];
            selected = original = null;
            editing = pickingLocation = false;
            status = null;
            focusError = false;
        }

        if (failure.Status == 403) canWrite = false;
    }

    /**
     * Removes cached data when a read no longer authorizes it, without discarding drafts on write denial.
     * @param failure The failed catalogue read.
     * @param attractionId The detail record requested, or null for a collection read.
     */
    void HandleReadFailure(ApiError failure, Guid? attractionId = null)
    {
        ShowError(failure);
        if (failure.Status == 403)
        {
            results = [];
            selected = original = null;
            editing = pickingLocation = false;
            draft = new();
            status = null;
        }
        else if (failure.Status == 404 && attractionId is { } id)
        {
            results = results.Where(item => item.AttractionId != id).ToArray();
            if (selected?.AttractionId == id) selected = null;
        }
    }

    /**
     * Records an attraction request failure without any identity or token data.
     * @param operation The operation that failed.
     * @param failure The classified failure.
     */
    void LogFailure(string operation, ApiError failure) => Logger.LogWarning("Attractions {Operation} failed as {Kind} (status {Status})", operation, failure.Kind, failure.Status);

    void ClearError()
    {
        error = null;
        errors = new Dictionary<string, string[]>();
        retrySearch = false;
        focusError = false;
    }

    bool Current(int version) => !disposed && version == generation;

    (CancellationTokenSource Source, int Version) StartRequest()
    {
        CancelRequest();
        request = new();
        return (request, generation);
    }

    void CancelRequest()
    {
        generation++;
        request?.Cancel();
        request?.Dispose();
        request = null;
    }

    /** Unsubscribes account notifications and prevents late HTTP responses from changing a closed view. */
    public void Dispose()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        CancelRequest();
    }
}
