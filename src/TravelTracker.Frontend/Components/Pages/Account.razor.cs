using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Users;
using TravelTracker.Frontend.Components.Shared;
using TravelTracker.Frontend.Services;

namespace TravelTracker.Frontend.Components.Pages;

/** Displays and edits only the authenticated caller's Users profile. */
public partial class Account : IDisposable
{
    [Inject] AuthSetup Setup { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] UsersClient Users { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] NavigationManager Navigation { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] ILogger<Account> Logger { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "signin")]
    string? SignInFailure { get; set; }

    /** Set to "local" when the TravelTracker session ended but provider logout could not be started. */
    [SupplyParameterFromQuery(Name = "signedout")]
    string? SignedOut { get; set; }

    SelfUserProfile? profile;
    ProfileEditModel? editModel;
    EditContext? editContext;
    ValidationMessageStore? serverValidation;
    CancellationTokenSource? request;
    ITimer? expiryTimer;
    string? sessionMessage, loadFailure, saveError, statusMessage;
    string? signInEmail;
    string loadFailureTitle = "Unable to load your profile";
    FeedbackKind loadFailureKind = FeedbackKind.Unexpected;
    bool loaded, loadingProfile, signedIn, editing, busy, writeDenied, disposed, noAccount;
    int generation;
    HashSet<string> grantedScopes = new(StringComparer.Ordinal);

    bool CanEdit => profile is { Status: AdminAccessState.ActiveStatus } && HasScope("users.write") && !writeDenied;

    PasswordChangeModel? passwordModel;
    EditContext? passwordContext;
    string? passwordError, passwordStatus;
    bool passwordBusy;

    protected override async Task OnInitializedAsync()
    {
        if (!Setup.Configured)
        {
            loaded = true;
            return;
        }

        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        var version = ++generation;
        await ApplyAuthenticationStateAsync(Authentication.GetAuthenticationStateAsync(), version);
    }

    /**
     * Applies a new host-authenticated state and clears profile values from the preceding session.
     * @param state The replacement or revoked authentication state.
     * @param version The generation captured when the state changed.
     * @return Completion of the matching profile load, if the new session can read Users.
     */
    async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> state, int version)
    {
        var user = (await state).User;
        if (!Current(version)) return;

        expiryTimer?.Dispose();
        expiryTimer = null;
        var previouslySignedIn = signedIn;
        signedIn = AccessTokenClaims.IsSignedIn(user, Sessions, Clock.GetUtcNow());
        signInEmail = signedIn ? user.FindFirstValue("email") ?? user.FindFirstValue(ClaimTypes.Email) : null;
        grantedScopes = AccessTokenClaims.GrantedScopes(user);
        request?.Cancel();
        profile = null;
        editModel = null;
        ClearEditContext();
        serverValidation = null;
        editing = false;
        busy = false;
        writeDenied = false;
        saveError = null;
        statusMessage = null;
        ClearPasswordForm();
        loadFailure = null;
        noAccount = false;

        if (!signedIn)
        {
            if (previouslySignedIn || user.Identity?.IsAuthenticated == true)
                sessionMessage = "Your session has expired. Sign in again to continue.";
            loadingProfile = false;
            loaded = true;
            return;
        }

        sessionMessage = null;
        ScheduleSessionExpiry(user, version);
        if (!HasScope("users.read"))
        {
            loadFailureTitle = "Profile permission required";
            loadFailureKind = FeedbackKind.Permission;
            loadFailure = "This session does not include permission to read your profile. Sign out, request the Users profile permission, and sign in again.";
            loadingProfile = false;
            loaded = true;
            return;
        }

        await LoadProfileAsync(version);
    }

    void AuthenticationChanged(Task<AuthenticationState> state) => _ = AuthenticationChangedAsync(state);

    /**
     * Applies an account change on the render thread; failures are logged instead of left unobserved.
     * @param state The replacement or revoked authentication state.
     * @return Completion of the reset and the matching profile load.
     */
    async Task AuthenticationChangedAsync(Task<AuthenticationState> state)
    {
        if (disposed) return;
        try
        {
            await InvokeAsync(async () =>
            {
                var version = ++generation;
                await ApplyAuthenticationStateAsync(state, version);
                if (!disposed) StateHasChanged();
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Account authentication change failed ({FailureType})", exception.GetType().Name);
        }
    }

    /**
     * Schedules a fresh authentication check for the current access token's expiry.
     * @param user The server-authenticated principal whose profile is displayed.
     * @param version The authentication generation that owns this timer.
     */
    void ScheduleSessionExpiry(ClaimsPrincipal user, int version)
    {
        if (!long.TryParse(user.FindFirstValue(AccessTokenClaims.ExpiresAt), out var expiresAt)) return;
        var delay = DateTimeOffset.FromUnixTimeSeconds(expiresAt) - Clock.GetUtcNow();
        expiryTimer = Clock.CreateTimer(_ => _ = RevalidateFromTimerAsync(version), null,
            delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    /**
     * Clears profile data when an idle page reaches the access token's expiry.
     * @param version The authentication generation scheduled for expiry.
     * @return Completion of the refreshed authentication and profile state.
     */
    async Task RevalidateExpiredSessionAsync(int version)
    {
        if (!Current(version)) return;
        var nextVersion = ++generation;
        await ApplyAuthenticationStateAsync(Authentication.GetAuthenticationStateAsync(), nextVersion);
        if (!disposed) StateHasChanged();
    }

    /**
     * Runs a scheduled expiry check on the render context and logs a failure instead of leaving it unobserved.
     * @param version The authentication generation that scheduled the timer.
     * @return Completion of the expiry check.
     */
    async Task RevalidateFromTimerAsync(int version)
    {
        if (disposed) return;
        try
        {
            await InvokeAsync(() => RevalidateExpiredSessionAsync(version));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogError("Session expiry check failed ({FailureType})", exception.GetType().Name);
        }
    }

    /**
     * Loads the server-confirmed own profile over the Users public HTTP contract.
     * @param version The authentication generation authorized to accept the result.
     * @return Completion of the guarded GET operation.
     */
    async Task LoadProfileAsync(int version)
    {
        var current = BeginRequest();
        loadingProfile = true;
        loadFailure = null;
        noAccount = false;
        try
        {
            var result = await Users.GetMeAsync(current.Token);
            if (!Current(version) || current.IsCancellationRequested) return;
            if (result is null)
            {
                noAccount = true;
                Logger.LogInformation("Profile page opened for a sign-in with no Users account yet");
                return;
            }

            profile = result;
            ResetPasswordForm();
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded or closed, nothing to publish
        }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                if (failure.Status == 401)
                {
                    signedIn = false;
                    profile = null;
                    ClearPasswordForm();
                    sessionMessage = "Your session was rejected. Sign in again to continue.";
                }
                SetLoadFailure(failure.Kind, failure.Status == 403
                    ? "Your account does not have access to a profile. Contact support if you believe this is an error."
                    : failure.Message, failure.Status);
            }
        }
        catch (Exception exception)
        {
            if (Current(version))
            {
                Logger.LogError("Users profile request failed unexpectedly ({FailureType})", exception.GetType().Name);
                SetLoadFailure(FailureKind.Unexpected, "Your profile could not be loaded. Try again.");
            }
        }
        finally
        {
            if (Current(version))
            {
                loadingProfile = false;
                loaded = true;
            }
            EndRequest(current);
        }
    }

    /** Retries a profile read without changing the current session. */
    Task RetryLoadAsync()
    {
        if (!signedIn || !HasScope("users.read") || loadingProfile) return Task.CompletedTask;
        var version = generation;
        return LoadProfileAsync(version);
    }

    /** Opens a form initialized from the latest server-confirmed profile values. */
    void BeginEdit()
    {
        if (!CanEdit || profile is null || busy) return;
        ClearEditContext();
        editModel = new ProfileEditModel(profile);
        editContext = new EditContext(editModel);
        serverValidation = new ValidationMessageStore(editContext);
        editContext.OnFieldChanged += OnEditFieldChanged;
        saveError = null;
        statusMessage = null;
        editing = true;
    }

    /** Closes the editor without sending a request. */
    void CancelEdit()
    {
        if (busy) return;
        ClearEditContext();
        editing = false;
        saveError = null;
        statusMessage = null;
    }

    void OnEditFieldChanged(object? _sender, FieldChangedEventArgs eventArgs)
    {
        serverValidation?.Clear(eventArgs.FieldIdentifier);
        saveError = null;
        statusMessage = null;
        editContext?.NotifyValidationStateChanged();
    }

    /**
     * Sends changed writable fields only and waits for the Users API's saved representation.
     * @return Completion of the guarded profile PATCH operation.
     */
    async Task SaveAsync()
    {
        if (busy || writeDenied || !CanEdit || profile is null || editModel is null) return;
        if (!editModel.HasChanges) return;

        var version = generation;
        var current = BeginRequest();
        busy = true;
        saveError = null;
        statusMessage = null;
        serverValidation?.Clear();
        editContext?.NotifyValidationStateChanged();
        try
        {
            var update = new UpdateSelfProfileRequest(editModel.Address);
            var confirmed = await Users.UpdateMeAsync(profile, update, current.Token);
            if (!Current(version) || current.IsCancellationRequested) return;
            profile = confirmed;
            ClearEditContext();
            editing = false;
            statusMessage = "Your profile has been updated.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded or closed, nothing to publish
        }
        catch (ApiError failure)
        {
            if (!Current(version)) return;
            if (failure.Status == 401)
            {
                signedIn = false;
                profile = null;
                editModel = null;
                ClearEditContext();
                sessionMessage = "Your session was rejected. Sign in again to continue.";
                editing = false;
                return;
            }

            if (failure.Status == 403)
            {
                writeDenied = true;
                saveError = "Your account or session no longer permits profile changes. Your entered values are still here; copy them before leaving this form.";
                await RecheckProfileReadAccessAsync(version, current.Token);
                return;
            }

            if (failure.Status is 400 or 422)
            {
                foreach (var (field, errors) in failure.Errors)
                {
                    var identifier = field.ToLowerInvariant() switch
                    {
                        "address" => editContext?.Field(nameof(ProfileEditModel.Address)),
                        _ => null
                    };
                    if (identifier is { } fieldIdentifier)
                        serverValidation?.Add(fieldIdentifier, errors);
                }
                editContext?.NotifyValidationStateChanged();
                saveError = failure.Errors.Count == 0
                    ? failure.Message
                    : "Check the highlighted fields and try again.";
                return;
            }

            saveError = failure.Status == 409
                ? "Your profile changed. Review the fields and try again."
                : failure.Message;
        }
        catch (Exception exception)
        {
            if (!Current(version)) return;
            Logger.LogError("Users profile update failed unexpectedly ({FailureType})", exception.GetType().Name);
            saveError = "Your profile could not be updated. Your entered values are still here; try again.";
        }
        finally
        {
            if (Current(version)) busy = false;
            EndRequest(current);
        }
    }

    /**
     * Rechecks profile read access after the Users API refuses a write.
     * @param version The authentication generation authorized to accept the result.
     * @param cancellationToken Cancels the profile recheck with the original save request.
     * @return Completion of the read-access check and any required denied-state update.
     */
    async Task RecheckProfileReadAccessAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await Users.GetMeAsync(cancellationToken);
            if (!Current(version) || cancellationToken.IsCancellationRequested) return;
            if (latest is null)
            {
                ClearDeniedProfile();
                return;
            }
            profile = latest;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // superseded or closed, nothing to publish
        }
        catch (ApiError failure) when (failure.Status == 403)
        {
            if (Current(version)) ClearDeniedProfile();
        }
        catch (ApiError failure) when (failure.Status == 401)
        {
            if (!Current(version)) return;
            signedIn = false;
            profile = null;
            ClearEditContext();
            editing = false;
            sessionMessage = "Your session was rejected. Sign in again to continue.";
        }
        catch (Exception exception)
        {
            if (Current(version)) Logger.LogWarning("Users profile access recheck failed unexpectedly ({FailureType})", exception.GetType().Name);
        }
    }

    /**
     * Removes profile and draft data after a fresh Users profile read is denied.
     */
    void ClearDeniedProfile()
    {
        profile = null;
        ClearEditContext();
        ClearPasswordForm();
        editing = false;
        writeDenied = false;
        saveError = null;
        statusMessage = null;
        SetLoadFailure(FailureKind.Permission, "Your account no longer has access to this profile. Contact support if you believe this is an error.", 403);
        loaded = true;
        loadingProfile = false;
    }

    void SetLoadFailure(FailureKind kind, string message, int? status = null)
    {
        loadFailureKind = status == 403 ? FeedbackKind.Permission : kind switch
        {
            FailureKind.Network => FeedbackKind.Network,
            FailureKind.Unavailable => FeedbackKind.Unavailable,
            FailureKind.Permission => FeedbackKind.Permission,
            _ => FeedbackKind.Unexpected
        };
        loadFailureTitle = status == 403 ? "Profile access denied" : loadFailureKind switch
        {
            FeedbackKind.Network => "Connection error",
            FeedbackKind.Unavailable => "Users service unavailable",
            FeedbackKind.Permission => "Sign-in required",
            _ => "Unable to load your profile"
        };
        loadFailure = message;
    }

    CancellationTokenSource BeginRequest()
    {
        request?.Cancel();
        var current = new CancellationTokenSource();
        request = current;
        return current;
    }

    void EndRequest(CancellationTokenSource current)
    {
        if (ReferenceEquals(request, current)) request = null;
        current.Dispose();
    }

    bool Current(int version) => !disposed && version == generation;

    bool HasScope(string permission) => grantedScopes.Contains(permission);

    static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;

    static string RoleLabel(string role) => role switch
    {
        AdminAccessState.AdministratorRole => "Administrator",
        _ => "Traveller"
    };

    static string StatusLabel(string status) => status switch
    {
        AdminAccessState.ActiveStatus => "Active",
        "banned" => "Read-only",
        "deactivated" => "Deactivated",
        _ => "Unavailable"
    };

    /** Leaves the circuit for the server endpoint that starts hosted sign-in. */
    void SignIn() => Navigation.NavigateTo("auth/login", forceLoad: true);

    void ClearEditContext()
    {
        if (editContext is not null) editContext.OnFieldChanged -= OnEditFieldChanged;
        editContext = null;
        serverValidation = null;
        editModel = null;
    }

    /** Starts an empty password form for the loaded profile; typed values never survive a session change. */
    void ResetPasswordForm()
    {
        passwordModel = new PasswordChangeModel();
        passwordContext = new EditContext(passwordModel);
        passwordError = null;
        passwordStatus = null;
    }

    /** Drops password values and messages, for example when the session ends or the profile is cleared. */
    void ClearPasswordForm()
    {
        passwordModel = null;
        passwordContext = null;
        passwordError = null;
        passwordStatus = null;
        passwordBusy = false;
    }

    /**
     * Sends the password change and reports success only after the Users API confirms it. Profile fields are not touched.
     * @return Completion of the guarded password change request.
     */
    async Task ChangePasswordAsync()
    {
        if (passwordBusy || passwordModel is null || !CanEdit) return;

        var version = generation;
        var current = BeginRequest();
        var model = passwordModel;
        passwordBusy = true;
        passwordError = null;
        passwordStatus = null;
        try
        {
            await Users.ChangePasswordAsync(model.CurrentPassword, model.NewPassword, current.Token);
            if (!Current(version) || current.IsCancellationRequested) return;
            ResetPasswordForm();
            passwordStatus = "Your password has been changed. Your session stays signed in.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
            // superseded or closed, nothing to publish
        }
        catch (ApiError failure)
        {
            if (!Current(version)) return;
            if (failure.Status == 401)
            {
                signedIn = false;
                profile = null;
                ClearEditContext();
                ClearPasswordForm();
                editing = false;
                sessionMessage = "Your session was rejected. Sign in again to continue.";
                return;
            }

            passwordError = failure.Status == 403
                ? "Your account or session no longer permits password changes. Your entered values are still here."
                : failure.Errors.Count > 0
                    ? string.Join(" ", failure.Errors.Values.SelectMany(errors => errors))
                    : failure.Message;
        }
        catch (Exception exception)
        {
            if (!Current(version)) return;
            Logger.LogError("Password change failed unexpectedly ({FailureType})", exception.GetType().Name);
            passwordError = "Your password could not be changed. Try again.";
        }
        finally
        {
            if (Current(version)) passwordBusy = false;
            EndRequest(current);
        }
    }

    public void Dispose()
    {
        disposed = true;
        generation++;
        expiryTimer?.Dispose();
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
        ClearEditContext();
        ClearPasswordForm();
    }
}
