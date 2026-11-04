using Microsoft.AspNetCore.Components.Authorization;
using TravelTracker.Frontend.Auth;

namespace TravelTracker.Frontend.Api;

/**
 * Sends the signed-in user's access token only to a named, configured service.
 *
 * @param authentication Supplies the current user, re-read after every await so a sign-out wins races.
 * @param directory Resolves and validates the destination URL.
 * @param client Performs the bounded request.
 * @param timeProvider The clock used to decide whether the token has expired.
 * @param logger Receives expired and rejected sessions, never token values.
 * @param sessions Checks and revokes the shared server session across cookies and circuits.
 */
public sealed class ProtectedClient(AuthenticationStateProvider authentication, ServiceDirectory directory, ServiceClient client, TimeProvider timeProvider, ILogger<ProtectedClient> logger, SessionRegistry sessions)
{
    /**
     * Reads a protected JSON resource.
     *
     * @param service The configured service name.
     * @param path A relative versioned API path.
     * @param cancellationToken Cancels the request when the view closes.
     * @return The parsed response; throws ApiError with Permission and 401 for a missing, expired or rejected session, and 403 for a missing permission.
     */
    public async Task<T?> GetJsonAsync<T>(string service, string path, CancellationToken cancellationToken = default)
    {
        var url = directory.ProtectedUrlFor(service, path);
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        var sessionId = SessionRegistry.IdFor(user);
        var token = sessions.IsActive(user) ? AccessTokenClaims.ActiveToken(user, timeProvider.GetUtcNow()) : null;
        if (token is null)
            throw SignedOut();
        T? result;
        try
        {
            result = await client.GetJsonAsync<T>(url, cancellationToken, token);
        }
        catch (ApiError error) when (error.Status == 401)
        {
            // Revoke the session that sent this request, never a later replacement sign-in.
            sessions.Revoke(sessionId);
            logger.LogWarning("{Service} rejected the access token", service);
            throw new ApiError(FailureKind.Permission, "Your session was rejected. Sign in again.", 401);
        }
        catch (ApiError error) when (error.Status == 403)
        {
            throw new ApiError(FailureKind.Permission, "You do not have permission for this action.", 403);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // a sign-out while the request was in flight must not let a late success through
        var currentUser = (await authentication.GetAuthenticationStateAsync()).User;
        if (!sessions.IsActive(user) || !sessions.IsActive(currentUser) || SessionRegistry.IdFor(currentUser) != sessionId)
            throw SignedOut();
        return result;
    }

    static ApiError SignedOut() => new(FailureKind.Permission, "Sign in to continue.", 401);
}
