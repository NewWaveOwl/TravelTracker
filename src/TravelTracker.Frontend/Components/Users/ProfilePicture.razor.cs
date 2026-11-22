using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Forms;
using TravelTracker.Frontend.Api;
using TravelTracker.Frontend.Api.Users;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Components.Users;

/** Displays only a server-confirmed owner's private picture and saves photo selections independently of text edits. */
public partial class ProfilePicture : IDisposable
{
    [Inject] UsersClient Users { get; set; } = default!;
    [Inject] AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] SessionRegistry Sessions { get; set; } = default!;
    [Inject] TimeProvider Clock { get; set; } = default!;
    [Inject] ILogger<ProfilePicture> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public SelfUserProfile Profile { get; set; } = default!;
    [Parameter] public bool Editable { get; set; }
    [Parameter] public bool Editing { get; set; }
    [Parameter] public bool Disabled { get; set; }

    readonly string hintId = $"picture-help-{Guid.NewGuid():N}";
    Guid? owner;
    string? ownerSession, pictureSource, error, status;
    string appearance = "failure";
    bool authorityValid, canWrite, loading, uploading, disposed;
    int operationVersion, fileVersion;
    CancellationTokenSource? request;
    ITimer? expiryTimer;

    bool CanChoose => authorityValid && canWrite && Editable && Profile.Status == "active";
    string DisplayName => !authorityValid || string.IsNullOrWhiteSpace(Profile.Username) ? "Your profile" : Profile.Username;
    string Initials
    {
        get
        {
            if (!authorityValid) return "TT";
            var initials = string.Concat((Profile.Username ?? "").Split([' ', '_', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Take(2).Select(part => StringInfo.GetNextTextElement(part).ToUpperInvariant()));
            return initials.Length == 0 ? "TT" : initials;
        }
    }

    protected override void OnInitialized()
    {
        Authentication.AuthenticationStateChanged += AuthenticationChanged;
        Sessions.Revoked += SessionRevoked;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (owner == Profile.PublicId)
        {
            if (Profile.Status == "deactivated") Invalidate();
            else if (Profile.Status != "active" && uploading)
            {
                Interlocked.Increment(ref operationVersion);
                request?.Cancel();
                uploading = false;
                fileVersion++;
            }
            return;
        }
        ClearPrivateState();
        owner = Profile.PublicId;
        var version = operationVersion;
        var user = (await Authentication.GetAuthenticationStateAsync()).User;
        if (disposed || version != operationVersion || !AccessTokenClaims.IsSignedIn(user, Sessions, Clock.GetUtcNow()) ||
            !AccessTokenClaims.GrantedScopes(user).Contains("users.read") || Profile.Status == "deactivated") return;
        ownerSession = SessionRegistry.IdFor(user);
        authorityValid = true;
        canWrite = AccessTokenClaims.GrantedScopes(user).Contains("users.write");
        if (long.TryParse(user.FindFirstValue(AccessTokenClaims.ExpiresAt), out var seconds))
        {
            var delay = DateTimeOffset.FromUnixTimeSeconds(seconds) - Clock.GetUtcNow();
            expiryTimer = Clock.CreateTimer(_ => Invalidate(), null, delay > TimeSpan.Zero ? delay : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
        await ReloadAsync();
    }

    void AuthenticationChanged(Task<AuthenticationState> _) => Invalidate();
    void SessionRevoked(string session) { if (session == ownerSession) Invalidate(); }

    /** Invalidates pending binary work before dispatching the private-state reset onto the render context. */
    void Invalidate()
    {
        if (disposed) return;
        Interlocked.Increment(ref operationVersion);
        try { request?.Cancel(); } catch (ObjectDisposedException) { }
        _ = InvalidateOnRenderAsync();
    }

    /** @return Completion of the guarded render-context reset after authentication or expiry changes. */
    async Task InvalidateOnRenderAsync()
    {
        try
        {
            await InvokeAsync(() =>
            {
                if (disposed) return;
                ClearPrivateState();
                StateHasChanged();
            });
        }
        catch (Exception exception)
        {
            if (!disposed) Logger.LogWarning("Profile picture state reset failed ({FailureType})", exception.GetType().Name);
        }
    }

    /**
     * Reads the fixed private picture route; no owner IDs, filenames or credentials enter its image URL.
     * @return Completion of the read and its safe feedback, while retaining a prior picture on transient failure.
     */
    async Task ReloadAsync()
    {
        if (!authorityValid || disposed || loading || uploading) return;
        var (current, version) = BeginRequest();
        loading = true;
        error = status = null;
        try { await ReadPictureAsync(current.Token, version, false); }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure) { if (Current(version)) ShowFailure(failure, true); }
        catch (Exception exception) { if (Current(version)) Unexpected(exception); }
        finally
        {
            if (Current(version)) loading = false;
            EndRequest(current);
        }
    }

    /**
     * Saves one bounded native file selection as raw PNG/JPEG and refreshes only after a confirmed 204.
     * @param selection The browser-selected file; its name is never sent or logged.
     * @return Completion of upload/refresh, with the previous picture retained on rejection.
     */
    async Task UploadAsync(InputFileChangeEventArgs selection)
    {
        if (!CanChoose || !Editing || Disabled || loading || uploading || disposed) return;
        var (current, version) = BeginRequest();
        uploading = true;
        error = status = null;
        byte[]? buffer = null, payload = null;
        var confirmed = false;
        try
        {
            if (selection.FileCount != 1) throw new ApiError(FailureKind.Validation, "Choose one static PNG or JPEG photo.");
            var file = selection.File;
            if (file.Size is < 1 or > UsersClient.MaxPictureUploadBytes)
                throw new ApiError(FailureKind.Validation, "Choose a photo no larger than 1 MiB (1,048,576 bytes).", 413);
            var contentType = file.ContentType.ToLowerInvariant();
            if (contentType is not ("image/png" or "image/jpeg"))
                throw new ApiError(FailureKind.Validation, "Choose one static PNG or JPEG photo.", 415);
            buffer = new byte[UsersClient.MaxPictureUploadBytes + 1];
            await using var input = file.OpenReadStream(UsersClient.MaxPictureUploadBytes, current.Token);
            var count = await input.ReadAtLeastAsync(buffer, buffer.Length, false, current.Token);
            if (!Current(version)) return;
            if (count is < 1 or > UsersClient.MaxPictureUploadBytes)
                throw new ApiError(FailureKind.Validation, "Choose a photo no larger than 1 MiB (1,048,576 bytes).", 413);
            payload = buffer[..count];
            await Users.ReplacePictureAsync(payload, contentType, ownerSession, current.Token);
            if (!Current(version)) return;
            confirmed = true;
            status = "Photo saved. This change is independent of profile text changes.";
            await ReadPictureAsync(current.Token, version, true);
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested) { }
        catch (ApiError failure)
        {
            if (!Current(version)) return;
            ShowFailure(failure, confirmed);
            if (!confirmed && failure.Status == 403)
            {
                // A denied write can still leave read access (for example a banned account).
                try { await ReadPictureAsync(current.Token, version, false); }
                catch (OperationCanceledException) when (current.IsCancellationRequested) { }
                catch (ApiError readFailure) { if (Current(version) && readFailure.Status is 401 or 403) ShowFailure(readFailure, true); }
            }
            if (confirmed && authorityValid) error = "Your photo was saved, but its preview could not be refreshed. Reload the picture to view it.";
        }
        catch (IOException)
        {
            if (Current(version)) { error = "The selected file could not be read. Choose a static PNG or JPEG up to 1 MiB."; appearance = "validation"; }
        }
        catch (Exception exception) { if (Current(version)) Unexpected(exception); }
        finally
        {
            if (buffer is not null) Array.Clear(buffer);
            if (payload is not null) Array.Clear(payload);
            if (Current(version)) { uploading = false; fileVersion++; }
            EndRequest(current);
        }
    }

    /**
     * Publishes a normalized PNG only for the current owner and releases the temporary binary buffer.
     * @param cancellationToken Cancels the read with its owning operation.
     * @param version The operation generation authorized to publish the response.
     * @param afterUpload Whether an absent preview follows an already-confirmed upload.
     * @return Completion of the protected read and guarded in-memory preview replacement.
     */
    async Task ReadPictureAsync(CancellationToken cancellationToken, int version, bool afterUpload)
    {
        var bytes = await Users.GetPictureAsync(ownerSession, cancellationToken);
        try
        {
            if (!Current(version)) return;
            if (afterUpload && bytes is null) throw new ApiError(FailureKind.Unexpected, "The saved photo preview is not available.");
            pictureSource = bytes is null ? null : "data:image/png;base64," + Convert.ToBase64String(bytes);
        }
        finally { if (bytes is not null) Array.Clear(bytes); }
    }

    void ShowFailure(ApiError failure, bool readFailure)
    {
        appearance = failure.Kind == FailureKind.Validation ? "validation" : failure.Kind == FailureKind.Permission ? "permission" : "failure";
        error = failure.Status == 404 ? "Profile picture uploads are not available right now. Try again later."
            : failure.Status == 409 ? "Your profile changed during the upload. Reload the picture before trying again." : failure.Message;
        if (failure.Status == 403) { canWrite = false; fileVersion++; }
        if (failure.Status == 401 || readFailure && failure.Status == 403)
        {
            pictureSource = null;
            fileVersion++;
            authorityValid = false;
            loading = uploading = false;
            status = null;
        }
    }

    void Unexpected(Exception exception)
    {
        Logger.LogWarning("Profile picture operation failed ({FailureType})", exception.GetType().Name);
        error = "The profile picture operation could not be completed. Reload the picture before trying another upload.";
        appearance = "failure";
    }

    (CancellationTokenSource Request, int Version) BeginRequest()
    {
        request?.Cancel();
        var current = request = new CancellationTokenSource();
        return (current, Interlocked.Increment(ref operationVersion));
    }
    bool Current(int version) => !disposed && authorityValid && version == operationVersion;
    void EndRequest(CancellationTokenSource current) { if (ReferenceEquals(request, current)) request = null; current.Dispose(); }

    void ClearPrivateState()
    {
        Interlocked.Increment(ref operationVersion);
        request?.Cancel();
        expiryTimer?.Dispose();
        expiryTimer = null;
        ownerSession = pictureSource = error = status = null;
        authorityValid = canWrite = loading = uploading = false;
        fileVersion++;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Authentication.AuthenticationStateChanged -= AuthenticationChanged;
        Sessions.Revoked -= SessionRevoked;
        ClearPrivateState();
    }
}
