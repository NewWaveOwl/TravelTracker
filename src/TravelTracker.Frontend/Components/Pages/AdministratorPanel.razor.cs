using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Pages;

/** Lists only service-authorized registered users and confirms audited role changes through public HTTP contracts. */
public partial class AdministratorPanel : IDisposable
{
    [Inject] UsersClient Client { get; set; } = default!;
    [Inject] AdminAccessState Access { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] ILogger<AdministratorPanel> Logger { get; set; } = default!;

    IReadOnlyList<UserProfile> users = [];
    SelfUserProfile? me;
    UserProfile? target;
    bool loading = true, allowed, busy, disposed, focusError, focusConfirmation;
    int generation;
    string reason = "";
    string? error, status;
    string errorTitle = "Unable to complete the request";
    string NewRole => target?.Role == AdminAccessState.AdministratorRole ? "traveller" : AdminAccessState.AdministratorRole;
    ElementReference errorSummary, confirmationHeading;
    CancellationTokenSource? request;

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool _firstRender)
    {
        if (focusError)
        {
            focusError = false;
            await errorSummary.FocusAsync();
        }

        if (focusConfirmation && target is not null)
        {
            focusConfirmation = false;
            await confirmationHeading.FocusAsync();
        }
    }

    /**
     * Checks live authority before requesting any private user list.
     * @return Completion of the current guarded panel refresh.
     */
    async Task LoadAsync()
    {
        if (busy) return;
        var (current, version) = StartRequest();
        loading = true; allowed = false; users = []; me = null; target = null; error = null; reason = "";
        try
        {
            var profile = await Access.RefreshAsync(current.Token);
            if (!Current(version)) return;
            if (profile is not { Role: AdminAccessState.AdministratorRole, Status: AdminAccessState.ActiveStatus }) return;
            var found = await Client.ListAsync(current.Token);
            if (!Current(version)) return;
            me = profile; users = found; allowed = true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded by a newer request, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version)) ShowFailure(failure);
        }
        finally
        {
            if (Current(version)) loading = false;
        }
    }

    /**
     * Labels accounts with their username, falling back to the public ID when none is set.
     * @param user The account returned by the administrator API.
     * @return The username or public ID.
     */
    static string AccountLabel(UserProfile user) => user.Username ?? user.UserId.ToString();

    bool LastActiveAdministrator(UserProfile user) => user is { Role: AdminAccessState.AdministratorRole, Status: AdminAccessState.ActiveStatus } && users.Count(candidate => candidate is { Role: AdminAccessState.AdministratorRole, Status: AdminAccessState.ActiveStatus }) <= 1;
    static bool InactiveTraveller(UserProfile user) => user.Role != AdminAccessState.AdministratorRole && user.Status != AdminAccessState.ActiveStatus;

    void BeginChange(UserProfile user)
    {
        if (!allowed || busy || LastActiveAdministrator(user) || InactiveTraveller(user)) return;
        target = user; reason = ""; error = null; status = null; focusConfirmation = true;
    }

    void CancelChange()
    {
        if (busy) return;
        target = null;
        reason = "";
        error = null;
    }

    /**
     * Sends one explicit role change, then reloads live authority and the list without optimistic role changes.
     * @return Completion of the guarded save and refresh; failures keep the confirmation reason.
     */
    async Task ConfirmAsync()
    {
        if (busy || !allowed || target is null) return;
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            error = "Enter a reason from 1 to 500 characters.";
            errorTitle = "Check the role-change reason";
            focusError = true;
            return;
        }
        var (current, version) = StartRequest();
        var selected = target;
        var desiredRole = NewRole;
        busy = true; error = null; status = null;
        try
        {
            var changed = await Client.ChangeRoleAsync(selected.UserId, desiredRole, selected.Role, reason.Trim(), current.Token);
            if (!Current(version)) return;
            if (changed.UserId != selected.UserId || changed.Role != desiredRole)
                throw new ApiError(FailureKind.Unexpected, "The role change was not confirmed. Refresh users before retrying.");
            // the previous list stays on screen until the refresh below succeeds
            target = null; reason = "";
            status = "Role change confirmed. Refreshing the registered users…";
            await RefreshAfterChangeAsync(current, version);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded by a newer request, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version)) ShowFailure(failure);
        }
        finally
        {
            if (Current(version)) busy = false;
        }
    }

    /**
     * Reloads authority and the list after a saved role change; a refresh failure keeps the saved-change status and offers a retry.
     * @param current The request source that owns this refresh.
     * @param version The generation the refresh must still match before it publishes.
     * @return Completion of the refresh; only failures other than 401 and 403 stay on the panel.
     */
    async Task RefreshAfterChangeAsync(CancellationTokenSource current, int version)
    {
        try
        {
            var profile = await Access.RefreshAsync(current.Token);
            if (!Current(version)) return;
            if (profile is not { Role: AdminAccessState.AdministratorRole, Status: AdminAccessState.ActiveStatus })
            {
                me = profile; users = []; allowed = false;
                status = "Role change confirmed. Your account no longer has access to the Administrator Panel.";
                return;
            }
            var refreshed = await Client.ListAsync(current.Token);
            if (!Current(version)) return;
            me = profile; users = refreshed; allowed = true; status = "Role change confirmed. The list shows the saved roles.";
        }
        catch (ApiError failure) when (Current(version) && failure.Status is not (401 or 403))
        {
            Logger.LogWarning("Role change saved but refreshing users failed as {Kind} (status {Status})", failure.Kind, failure.Status);
            status = "Role change confirmed.";
            error = "Role change saved. Refreshing users failed; retry.";
            errorTitle = "Unable to refresh users";
            focusError = true;
        }
    }

    void ShowFailure(ApiError failure)
    {
        Logger.LogWarning("Administrator panel request failed as {Kind} (status {Status})", failure.Kind, failure.Status);
        error = failure.Message;
        status = null;
        errorTitle = failure.Status == 409 ? "The role or administrator count changed" : "Unable to complete the request";
        focusError = true;
        if (failure.Status is not (401 or 403)) return;
        allowed = false;
        users = [];
        me = null;
        target = null;
        reason = "";
        Access.Clear();
    }

    void AuthenticationChanged(Task<AuthenticationState> _pending) => _ = AuthenticationChangedAsync();

    /**
     * Clears private panel state for the new account, then reloads it; failures are logged instead of left unobserved.
     * @return Completion of the reset and the guarded reload.
     */
    async Task AuthenticationChangedAsync()
    {
        if (disposed) return;
        try
        {
            await InvokeAsync(async () =>
            {
                CancelRequest(); users = []; target = null; me = null; allowed = false; reason = ""; status = error = null; busy = false; focusConfirmation = focusError = false;
                StateHasChanged();
                await LoadAsync();
                if (!disposed) StateHasChanged();
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Administrator panel account change failed ({FailureType})", exception.GetType().Name);
        }
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

    /** Cancels closed-panel requests and removes account-change notifications. */
    public void Dispose()
    {
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        CancelRequest();
        users = [];
        target = null;
        me = null;
    }
}
