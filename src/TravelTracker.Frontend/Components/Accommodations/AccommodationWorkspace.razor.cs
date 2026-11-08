using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Accommodations;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Accommodations;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Accommodations;

/** Coordinates accommodation search, creation and confirmed draft selection through public HTTP contracts. */
public partial class AccommodationWorkspace : IDisposable
{
    [Inject] AccommodationsClient Client { get; set; } = default!;
    [Inject] UsersClient Users { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Parameter] public EventCallback<AccommodationDto?> SelectionChanged { get; set; }
    [Parameter] public Guid? SelectedAccommodationId { get; set; }

    IReadOnlyList<AccommodationDto> results = [];
    AccommodationDto? selected;
    AccommodationDraft draft = new();
    string query = "";
    string? status, formError;
    ApiError? error;
    IReadOnlyDictionary<string, string[]> errors = new Dictionary<string, string[]>();
    bool signedIn, canRead, canWrite, canShare, loaded, searching, loadingDetails, showCreate, saving, busy, disposed, retrySearch;
    bool focusError, focusDetails, focusNameOnRender, focusAddOnRender;
    ElementReference errorSummary, createErrorSummary, detailsHeading, addButton, firstModalControl, lastModalControl;
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
        await UpdateAccessAsync(await Authentication.GetAuthenticationStateAsync());
        if (signedIn && canRead) await SearchAsync();
        loaded = true;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusNameOnRender && showCreate) { focusNameOnRender = false; await firstModalControl.FocusAsync(); }
        else if (focusAddOnRender && !showCreate) { focusAddOnRender = false; await addButton.FocusAsync(); }
        else if (focusError) { focusError = false; if (showCreate) await createErrorSummary.FocusAsync(); else await errorSummary.FocusAsync(); }
        if (focusDetails && selected is not null) { focusDetails = false; await detailsHeading.FocusAsync(); }
    }

    /**
     * Clears account-specific results and selection when the authenticated session changes.
     * @param state The new authentication state.
     */
    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        CancelRequest();
        results = []; selected = null; showCreate = false; busy = saving = searching = loadingDetails = false;
        status = formError = null; ClearError(); draft = new(); focusNameOnRender = focusAddOnRender = false;
        await SelectionChanged.InvokeAsync(null);
        await UpdateAccessAsync(await state);
        if (signedIn && canRead) await SearchAsync();
        loaded = true;
        StateHasChanged();
    });

    async Task UpdateAccessAsync(AuthenticationState state)
    {
        var user = state.User;
        signedIn = user.Identity?.IsAuthenticated == true && Sessions.IsActive(user) && AccessTokenClaims.ActiveToken(user, Clock.GetUtcNow()) is not null;
        var scopes = user.FindAll(AccessTokenClaims.GrantedScope).SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.Ordinal);
        canRead = signedIn && scopes.Contains("catalogue.read");
        canWrite = signedIn && scopes.Contains("catalogue.write");
        canShare = false;

        // The live Users profile controls only whether the shared option is shown. The catalogue API authorizes the write.
        if (signedIn && scopes.Contains("users.read"))
        {
            try
            {
                var profile = await Users.GetMeAsync();
                canShare = profile is { Role: "administrator", Status: "active" };
            }
            catch (ApiError) { canShare = false; }
        }
        if (!canShare && draft.Visibility == "shared") draft.Visibility = "private";
    }

    /**
     * Searches by name without letting an older response replace newer results.
     * @return Completion of the visible catalogue search.
     */
    async Task SearchAsync()
    {
        if (busy || !signedIn || !canRead) return;
        var (current, version) = StartRequest();
        searching = busy = true; loadingDetails = false; ClearError(); status = null;
        try
        {
            var found = await Client.SearchAsync(query, current.Token);
            if (Current(version)) results = found;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) await HandleReadFailure(failure, searchFailure: true); }
        finally { if (Current(version)) { searching = busy = false; loaded = true; } }
    }

    async Task ClearSearchAsync() { if (busy) return; query = ""; await SearchAsync(); }

    /**
     * Loads detail by public ID and treats hidden private records as unavailable.
     * @param id Public accommodation identifier from a visible result.
     * @return Completion of the detail request.
     */
    async Task ShowDetailsAsync(Guid id)
    {
        if (busy || !signedIn || !canRead) return;
        var (current, version) = StartRequest();
        loadingDetails = busy = true; ClearError(); status = null;
        try
        {
            var found = await Client.GetAsync(id, current.Token);
            if (Current(version)) { selected = found; focusDetails = true; }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) await HandleReadFailure(failure, searchFailure: false, detailId: id); }
        finally { if (Current(version)) loadingDetails = busy = false; }
    }

    async Task SelectCurrentAsync()
    {
        if (busy || selected is null || !signedIn) return;
        if (selected.AccommodationId != SelectedAccommodationId)
        {
            await SelectionChanged.InvokeAsync(selected);
            status = "Saved place selected for this trip draft.";
        }
    }

    void OpenCreate()
    {
        if (busy || !canWrite) return;
        CancelRequest(); busy = searching = loadingDetails = false;
        ClearError(); formError = status = null; draft = new(); showCreate = true; focusNameOnRender = true; focusAddOnRender = false;
    }

    void CloseCreate()
    {
        if (saving) return;
        showCreate = false; formError = null; errors = new Dictionary<string, string[]>(); focusAddOnRender = true;
    }

    /**
     * Closes the add dialog on Escape without interfering with other keyboard controls.
     * @param args The key pressed while focus is inside the modal.
     */
    void HandleModalKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape") CloseCreate();
    }

    /** Moves keyboard focus to the final control when tabbing backwards past the dialog start. */
    async Task FocusLastModalControl(FocusEventArgs _) { if (!saving) await lastModalControl.FocusAsync(); }

    /** Moves keyboard focus to the first form control when tabbing forward past the dialog end. */
    async Task FocusFirstModalControl(FocusEventArgs _) { if (!saving) await firstModalControl.FocusAsync(); }

    /**
     * Validates and creates once; only a confirmed response enters the trip draft.
     * @return Completion of the create and best-effort results refresh.
     */
    async Task CreateAsync()
    {
        if (saving || !canWrite || !signedIn) return;
        ClearError(); formError = null; status = null;
        errors = draft.Validate();
        if (errors.Count != 0)
        {
            error = new ApiError(FailureKind.Validation, "Correct the highlighted fields. Your entries have been kept.");
            formError = error.Message; focusError = true; return;
        }

        var (current, version) = StartRequest();
        saving = busy = true; searching = loadingDetails = false;
        try
        {
            var saved = await Client.CreateAsync(draft.ToRequest(), current.Token);
            if (!Current(version)) return;
            selected = saved;
            focusDetails = true;
            results = string.IsNullOrWhiteSpace(query)
                ? results.Append(saved).DistinctBy(item => item.AccommodationId).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.AccommodationId).ToArray()
                : results;
            showCreate = false; draft = new(); formError = null; errors = new Dictionary<string, string[]>();
            await SelectionChanged.InvokeAsync(saved);
            if (!Current(version)) return;
            status = "Place saved and selected for this trip draft.";
            try
            {
                var refreshed = await Client.SearchAsync(query, current.Token);
                if (Current(version)) results = refreshed;
            }
            catch (OperationCanceledException) when (current.IsCancellationRequested) { }
            catch (ApiError failure)
            {
                if (!Current(version)) return;
                if (failure.Status is 401 or 403)
                {
                    await HandleReadFailure(failure, searchFailure: true);
                    if (failure.Status == 403)
                        status = "Place saved, but catalogue access is now denied. It is not selected in this trip draft.";
                }
                else { error = failure; retrySearch = true; status = "Place saved and selected. Search results could not be refreshed; retry the search instead of submitting again."; }
            }
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                error = failure; formError = failure.Message; errors = failure.Errors; focusError = true;
                if (failure.Status == 401) HandleUnauthorized();
                if (failure.Status == 403) canWrite = false;
            }
        }
        finally { if (Current(version)) saving = busy = false; }
    }

    string? FieldError(string name) => errors.FirstOrDefault(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { } messages
        ? string.Join(" ", messages) : null;

    async Task HandleReadFailure(ApiError failure, bool searchFailure, Guid? detailId = null)
    {
        if (failure.Status == 401) { HandleUnauthorized(); return; }
        error = failure; errors = failure.Errors; focusError = true;
        if (searchFailure && failure.Status == 403)
        {
            results = []; selected = null; retrySearch = false;
            await SelectionChanged.InvokeAsync(null);
        }
        else if (searchFailure) retrySearch = failure.Status != 403;

        if (!searchFailure && failure.Status == 403)
        {
            results = []; selected = null; retrySearch = false;
            await SelectionChanged.InvokeAsync(null);
        }
        else if (!searchFailure && failure.Status == 404 && detailId is { } requestedId)
        {
            results = results.Where(item => item.AccommodationId != requestedId).ToArray();
            if (selected?.AccommodationId == requestedId) selected = null;
            if (SelectedAccommodationId == requestedId) await SelectionChanged.InvokeAsync(null);
        }
        if (failure.Status == 404 && !searchFailure)
            error = new ApiError(FailureKind.Unexpected, "This accommodation is unavailable. Refresh the catalogue and choose a visible result.", 404);
    }

    void HandleUnauthorized()
    {
        signedIn = canRead = canWrite = canShare = false; focusError = false;
        results = []; selected = null; showCreate = false; status = null; retrySearch = false;
        _ = SelectionChanged.InvokeAsync(null);
    }

    void ClearError() { error = null; errors = new Dictionary<string, string[]>(); retrySearch = false; focusError = false; }
    static string VisibilityLabel(string value) => value == "shared" ? "SHARED" : "PRIVATE";
    bool Current(int version) => !disposed && version == generation;
    (CancellationTokenSource Source, int Version) StartRequest() { CancelRequest(); request = new(); return (request, generation); }
    void CancelRequest() { generation++; request?.Cancel(); request?.Dispose(); request = null; }

    /** Unsubscribes session notifications and cancels work when the workspace leaves the page. */
    public void Dispose() { disposed = true; Authentication.AuthenticationStateChanged -= AuthenticationChanged; CancelRequest(); }
}
