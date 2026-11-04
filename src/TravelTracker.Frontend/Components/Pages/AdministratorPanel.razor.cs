using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
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

    IReadOnlyList<UserProfile> users = [];
    UserProfile? me, target;
    bool loading = true, allowed, busy, disposed, focusError, focusConfirmation;
    int generation;
    string reason = "";
    string? error, status;
    string ErrorTitle = "Unable to complete the request";
    string NewRole => target?.Role == "administrator" ? "traveller" : "administrator";
    ElementReference errorSummary, confirmationHeading;
    CancellationTokenSource? request;

    protected override async Task OnInitializedAsync()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        await LoadAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusError) { focusError = false; await errorSummary.FocusAsync(); }
        if (focusConfirmation && target is not null) { focusConfirmation = false; await confirmationHeading.FocusAsync(); }
    }

    /**
     * Checks live authority before requesting any private user list.
     * @return Completion of the current guarded panel refresh.
     */
    async Task LoadAsync()
    {
        if (busy) return;
        var (current, version) = StartRequest();
        loading = true; allowed = false; users = []; me = target = null; error = null; reason = "";
        try
        {
            var profile = await Access.RefreshAsync(current.Token);
            if (!Current(version)) return;
            if (profile is not { Role: "administrator", Status: "active" }) return;
            var found = await Client.ListAsync(current.Token);
            if (!Current(version)) return;
            me = profile; users = found; allowed = true;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) ShowFailure(failure); }
        finally { if (Current(version)) loading = false; }
    }

    bool LastActiveAdministrator(UserProfile user) => user is { Role: "administrator", Status: "active" } && users.Count(u => u is { Role: "administrator", Status: "active" }) <= 1;
    static bool InactiveTraveller(UserProfile user) => user.Role != "administrator" && user.Status != "active";

    void BeginChange(UserProfile user)
    {
        if (!allowed || busy || LastActiveAdministrator(user) || InactiveTraveller(user)) return;
        target = user; reason = ""; error = null; status = null; focusConfirmation = true;
    }

    void CancelChange() { if (!busy) { target = null; reason = ""; error = null; } }

    /**
     * Sends one explicit role change, then reloads live authority and the list without optimistic role changes.
     * @return Completion of the guarded save and refresh; failures keep the confirmation reason.
     */
    async Task ConfirmAsync()
    {
        if (busy || !allowed || target is null) return;
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            error = "Enter a reason from 1 to 500 characters."; ErrorTitle = "Check the role-change reason"; focusError = true; return;
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
            target = null; reason = ""; users = []; allowed = false;
            status = "Role change confirmed. Refreshing the registered users…";
            var profile = await Access.RefreshAsync(current.Token);
            if (!Current(version)) return;
            if (profile is not { Role: "administrator", Status: "active" })
            {
                me = profile; status = "Role change confirmed. Your account no longer has access to the Administrator Panel."; return;
            }
            var refreshed = await Client.ListAsync(current.Token);
            if (!Current(version)) return;
            me = profile; users = refreshed; allowed = true; status = "Role change confirmed. The list shows the saved roles.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) ShowFailure(failure); }
        finally { if (Current(version)) busy = false; }
    }

    void ShowFailure(ApiError failure)
    {
        error = failure.Message; ErrorTitle = failure.Status == 409 ? "The role or administrator count changed" : "Unable to complete the request"; focusError = true;
        if (failure.Status is 401 or 403) { allowed = false; users = []; me = target = null; reason = ""; Access.Clear(); }
    }

    void AuthenticationChanged(Task<AuthenticationState> pending) => _ = InvokeAsync(async () =>
    {
        CancelRequest(); users = []; target = me = null; allowed = false; reason = ""; status = error = null; busy = false; focusConfirmation = focusError = false;
        StateHasChanged();
        await LoadAsync();
        if (!disposed) StateHasChanged();
    });

    bool Current(int version) => !disposed && version == generation;
    (CancellationTokenSource Source, int Version) StartRequest() { CancelRequest(); request = new(); return (request, generation); }
    void CancelRequest() { generation++; request?.Cancel(); request?.Dispose(); request = null; }

    /** Cancels closed-panel requests and removes account-change notifications. */
    public void Dispose() { disposed = true; Authentication.AuthenticationStateChanged -= AuthenticationChanged; CancelRequest(); users = []; target = me = null; }
}
