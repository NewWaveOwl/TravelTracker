using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Api;

/**
 * Sends a session token only to a named, configured service and rejects late successes after sign-out.
 * @param authentication Supplies the current user.
 * @param directory Validates destination URLs.
 * @param client Performs bounded HTTP calls.
 * @param timeProvider Checks access-token expiry.
 * @param logger Records rejected sessions without token values.
 * @param sessions Checks shared server-session revocation.
 */
public sealed class ProtectedClient(AuthenticationStateProvider authentication, ServiceDirectory directory, ServiceClient client, TimeProvider timeProvider, ILogger<ProtectedClient> logger, SessionRegistry sessions)
{
    /**
     * Reads a protected public HTTP contract.
     * @param service Configured service name.
     * @param path Versioned relative API path.
     * @param cancellationToken View cancellation.
     * @return Parsed response, or ApiError for rejected requests or sessions.
     */
    public Task<T?> GetJsonAsync<T>(string service, string path, CancellationToken cancellationToken = default) =>
        SendJsonAsync<T>(service, HttpMethod.Get, path, null, cancellationToken);

    /**
     * Sends a protected public contract while preserving session revocation across awaits.
     * @param service Configured service name.
     * @param method GET, POST, PUT or PATCH.
     * @param path Versioned relative API path.
     * @param body Public request fields.
     * @param cancellationToken View cancellation.
     * @return Parsed response; 401 revokes only the session that made the request.
     */
    public async Task<T?> SendJsonAsync<T>(string service, HttpMethod method, string path, object? body, CancellationToken cancellationToken = default) =>
        (await SendCoreAsync<T>(service, method, path, body, cancellationToken)).Body;

    /**
     * Sends a protected public contract and keeps the entity tag, for replaces guarded by If-Match.
     * @param service Configured service name.
     * @param method GET, PUT, POST or PATCH.
     * @param path Versioned relative API path.
     * @param body Public request fields.
     * @param ifMatch Entity tag the service must match, or null when no precondition applies.
     * @param cancellationToken View cancellation.
     * @return Parsed body and entity tag; 401 revokes only the session that made the request.
     */
    public Task<ServiceResponse<T>> SendAsync<T>(string service, HttpMethod method, string path, object? body, string? ifMatch, CancellationToken cancellationToken = default) =>
        SendCoreAsync<T>(service, method, path, body, cancellationToken, ifMatch: ifMatch);

    /**
     * Sends the server-owned setup secret only to the configured Users bootstrap endpoint.
     * @param secret Operator-provisioned secret that never enters the browser.
     * @param cancellationToken Cancels a closed setup view.
     * @return The confirmed administrator profile.
     */
    public async Task<T?> BootstrapFirstAdministratorAsync<T>(string secret, CancellationToken cancellationToken = default) =>
        (await SendCoreAsync<T>("Users", HttpMethod.Post, "/api/v1/users/bootstrap-first-administrator", new { }, cancellationToken, secret)).Body;

    /**
     * Sends a PDF using the same session and destination safeguards as JSON contracts.
     * @param path Versioned Trips import path.
     * @param content Caller-owned PDF stream.
     * @param fileName Display filename supplied by the file picker.
     * @param cancellationToken Cancels the import.
     * @return The owner's reviewable extraction draft.
     */
    public Task<T?> UploadPdfAsync<T>(string path, Stream content, string fileName, CancellationToken cancellationToken) =>
        SendAuthorizedAsync("Trips", HttpMethod.Post, path,
            (url, token) => client.UploadPdfAsync<T>(url, content, fileName, cancellationToken, token), cancellationToken);

    /**
     * Retrieves a PDF only while the requesting session remains active.
     * @param path Versioned Trips document path.
     * @param cancellationToken Cancels the download.
     * @return The owner's temporary source document.
     */
    public Task<byte[]> GetPdfAsync(string path, CancellationToken cancellationToken) =>
        SendAuthorizedAsync("Trips", HttpMethod.Get, path, (url, token) => client.GetPdfAsync(url, cancellationToken, token), cancellationToken);

    /**
     * Cancels one temporary import through its owner-scoped HTTP contract.
     * @param path Versioned Trips import path.
     * @param cancellationToken Cancels cleanup.
     * @return Completion after acknowledged removal.
     */
    public async Task DeleteTicketAsync(string path, CancellationToken cancellationToken) =>
        _ = await SendAuthorizedAsync("Trips", HttpMethod.Delete, path, async (url, token) =>
        {
            await client.DeleteTicketAsync(url, cancellationToken, token);
            return true;
        }, cancellationToken);

    async Task<ServiceResponse<T>> SendCoreAsync<T>(string service, HttpMethod method, string path, object? body, CancellationToken cancellationToken, string? bootstrapSecret = null, string? ifMatch = null)
        => await SendAuthorizedAsync(service, method, path,
            (url, token) => client.SendAsync<T>(method, url, body, cancellationToken, token, bootstrapSecret, ifMatch), cancellationToken);

    /**
     * Exchanges image bytes through the same configured-origin and session-revocation boundary as JSON.
     * @param service The configured service name.
     * @param method GET or PUT.
     * @param path A fixed versioned relative API route.
     * @param body Raw upload bytes, or null for GET.
     * @param contentType PNG/JPEG upload media type, or null for GET.
     * @param maxResponseBytes Bound on the normalized response bytes.
     * @param expectedSessionId The rendered owner session, preventing an old file selection from reaching a replacement account.
     * @param cancellationToken Cancels work when the view or owner changes.
     * @return Bytes accepted only while the same authenticated session remains active.
     */
    public Task<byte[]> SendImageAsync(string service, HttpMethod method, string path, byte[]? body, string? contentType,
        int maxResponseBytes, string? expectedSessionId, CancellationToken cancellationToken = default) =>
        SendAuthorizedAsync(service, method, path,
            (url, token) => client.SendImageAsync(method, url, body, contentType, maxResponseBytes, token, cancellationToken), cancellationToken, expectedSessionId);

    async Task<T> SendAuthorizedAsync<T>(string service, HttpMethod method, string path, Func<string, string, Task<T>> send,
        CancellationToken cancellationToken, string? expectedSessionId = null)
    {
        var url = directory.ProtectedUrlFor(service, path);
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var sessionId = SessionRegistry.IdFor(user);
        var token = sessions.IsActive(user) ? AccessTokenClaims.ActiveToken(user, timeProvider.GetUtcNow()) : null;
        if (token is null || expectedSessionId is not null && expectedSessionId != sessionId) throw SignedOut();
        cancellationToken.ThrowIfCancellationRequested();
        T result;
        try
        {
            result = await send(url, token);
        }
        catch (ApiError error) when (error.Status == 401)
        {
            sessions.Revoke(sessionId);
            logger.LogWarning("{Service} rejected the access token", service);
            throw new ApiError(FailureKind.Permission, "Your session was rejected. Sign in again.", 401);
        }
        catch (ApiError error) when (error.Status == 403)
        {
            throw new ApiError(FailureKind.Permission, "You do not have permission for this action.", 403);
        }
        if (cancellationToken.IsCancellationRequested)
        {
            if (result is byte[] canceledImage) Array.Clear(canceledImage);
            cancellationToken.ThrowIfCancellationRequested();
        }
        var current = (await authentication.GetAuthenticationStateAsync()).User;
        if (cancellationToken.IsCancellationRequested)
        {
            if (result is byte[] canceledImage) Array.Clear(canceledImage);
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (!sessions.IsActive(user) || !sessions.IsActive(current) || SessionRegistry.IdFor(current) != sessionId)
        {
            if (result is byte[] staleImage) Array.Clear(staleImage);
            throw method == HttpMethod.Get ? SignedOut() : new ApiError(FailureKind.Permission, "You were signed out while saving. The save may have been applied; check before retrying.", 401);
        }
        return result;
    }

    static ApiError SignedOut() => new(FailureKind.Permission, "Sign in to continue.", 401);
}
