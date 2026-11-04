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
     * @param method GET, POST or PATCH.
     * @param path Versioned relative API path.
     * @param body Public request fields.
     * @param cancellationToken View cancellation.
     * @return Parsed response; 401 revokes only the session that made the request.
     */
    public async Task<T?> SendJsonAsync<T>(string service, HttpMethod method, string path, object? body, CancellationToken cancellationToken = default)
    {
        var url = directory.ProtectedUrlFor(service, path);
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var sessionId = SessionRegistry.IdFor(user);
        var token = sessions.IsActive(user) ? AccessTokenClaims.ActiveToken(user, timeProvider.GetUtcNow()) : null;
        if (token is null) throw SignedOut();
        T? result;
        try { result = await client.SendJsonAsync<T>(method, url, body, cancellationToken, token); }
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
        cancellationToken.ThrowIfCancellationRequested();
        var current = (await authentication.GetAuthenticationStateAsync()).User;
        if (!sessions.IsActive(user) || !sessions.IsActive(current) || SessionRegistry.IdFor(current) != sessionId)
            throw SignedOut();
        return result;
    }

    static ApiError SignedOut() => new(FailureKind.Permission, "Sign in to continue.", 401);
}
