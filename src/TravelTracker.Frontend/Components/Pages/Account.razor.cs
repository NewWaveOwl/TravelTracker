using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;
using TravelTracker.Frontend.Features.Users;
using TravelTracker.Frontend.Components.Shared;

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
    IReadOnlyDictionary<string, string[]> saveFieldErrors = new Dictionary<string, string[]>();
    string? sessionMessage, loadFailure, saveError, statusMessage;
    string loadFailureTitle = "Unable to load your profile";
    FeedbackKind loadFailureKind = FeedbackKind.Unexpected;
    bool loaded, loadingProfile, signedIn, editing, busy, writeDenied, disposed;
    int generation;
    HashSet<string> grantedScopes = new(StringComparer.Ordinal);

    bool CanEdit => profile is { Status: "active" } && HasScope("users.write") && !writeDenied;

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

        var previouslySignedIn = signedIn;
        signedIn = Sessions.IsActive(user) && AccessTokenClaims.ActiveToken(user, Clock.GetUtcNow()) is not null;
        grantedScopes = GrantedScopes(user);
        request?.Cancel();
        profile = null;
        editModel = null;
        editContext = null;
        serverValidation = null;
        saveFieldErrors = new Dictionary<string, string[]>();
        editing = false;
        busy = false;
        writeDenied = false;
        saveError = null;
        statusMessage = null;
        loadFailure = null;

        if (!signedIn)
        {
            if (previouslySignedIn || user.Identity?.IsAuthenticated == true)
                sessionMessage = "Your session has expired. Sign in again to continue.";
            loadingProfile = false;
            loaded = true;
            return;
        }

        sessionMessage = null;
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

    void AuthenticationChanged(Task<AuthenticationState> state) => _ = InvokeAsync(async () =>
    {
        var version = ++generation;
        await ApplyAuthenticationStateAsync(state, version);
        if (!disposed) StateHasChanged();
    });

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
        try
        {
            var result = await Users.GetMeAsync(current.Token);
            if (!Current(version) || current.IsCancellationRequested) return;
            if (result is null)
            {
                SetLoadFailure(FailureKind.Unexpected, "The Users service did not return a profile.");
                return;
            }

            profile = result;
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            if (Current(version))
            {
                if (failure.Status == 401)
                {
                    signedIn = false;
                    profile = null;
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
                Logger.LogError(exception, "Users profile request failed unexpectedly");
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
        editModel = new ProfileEditModel(profile);
        editContext = new EditContext(editModel);
        serverValidation = new ValidationMessageStore(editContext);
        editContext.OnFieldChanged += OnEditFieldChanged;
        saveFieldErrors = new Dictionary<string, string[]>();
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

    void OnEditFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        serverValidation?.Clear(args.FieldIdentifier);
        saveFieldErrors = new Dictionary<string, string[]>();
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
            var update = new UpdateSelfProfileRequest(editModel.Username, editModel.Email, editModel.Address);
            var confirmed = await Users.UpdateMeAsync(profile, update, current.Token);
            if (!Current(version) || current.IsCancellationRequested) return;
            profile = confirmed;
            ClearEditContext();
            editing = false;
            saveFieldErrors = new Dictionary<string, string[]>();
            statusMessage = "Your profile has been updated.";
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            if (!Current(version)) return;
            if (failure.Status == 401)
            {
                signedIn = false;
                profile = null;
                editModel = null;
                editContext = null;
                sessionMessage = "Your session was rejected. Sign in again to continue.";
                editing = false;
                return;
            }

            if (failure.Status == 403)
            {
                writeDenied = true;
                saveError = "Your account or session no longer permits profile changes. Your entered values are still here; copy them before leaving this form.";
                return;
            }

            if (failure.Status is 400 or 422)
            {
                var messages = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var (field, errors) in failure.Errors)
                {
                    var identifier = field.ToLowerInvariant() switch
                    {
                        "username" => editContext?.Field(nameof(ProfileEditModel.Username)),
                        "email" => editContext?.Field(nameof(ProfileEditModel.Email)),
                        "address" => editContext?.Field(nameof(ProfileEditModel.Address)),
                        _ => null
                    };
                    if (identifier is { } fieldIdentifier)
                        serverValidation?.Add(fieldIdentifier, errors);
                    messages[field] = errors;
                }
                saveFieldErrors = messages;
                editContext?.NotifyValidationStateChanged();
                saveError = messages.Count == 0
                    ? failure.Message
                    : "Check the highlighted fields and try again.";
                return;
            }

            saveError = failure.Status == 409
                ? "The username may already be in use, or your profile changed. Review the fields and try again."
                : failure.Message;
        }
        catch (Exception exception)
        {
            if (!Current(version)) return;
            Logger.LogError(exception, "Users profile update failed unexpectedly");
            saveError = "Your profile could not be updated. Your entered values are still here; try again.";
        }
        finally
        {
            if (Current(version)) busy = false;
            EndRequest(current);
        }
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

    static HashSet<string> GrantedScopes(ClaimsPrincipal user) => user.FindAll(AccessTokenClaims.GrantedScope)
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        .ToHashSet(StringComparer.Ordinal);

    static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;

    static string RoleLabel(string role) => role switch
    {
        "administrator" => "Administrator",
        _ => "Traveller"
    };

    static string StatusLabel(string status) => status switch
    {
        "active" => "Active",
        "banned" => "Read-only",
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

    public void Dispose()
    {
        disposed = true;
        generation++;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        request?.Cancel();
        request?.Dispose();
        ClearEditContext();
    }
}
