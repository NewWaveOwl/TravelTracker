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
 */
public sealed class ProtectedClient(AuthenticationStateProvider authentication, ServiceDirectory directory, ServiceClient client, TimeProvider timeProvider, ILogger<ProtectedClient> logger)
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
        var token = await ActiveTokenAsync() ?? throw SignedOut();
        T? result;
        try
        {
            result = await client.GetJsonAsync<T>(url, cancellationToken, token);
        }
        catch (ApiError error) when (error.Status == 401)
        {
            logger.LogWarning("{Service} rejected the access token", service);
            throw new ApiError(FailureKind.Permission, "Your session was rejected. Sign in again.", 401);
        }
        catch (ApiError error) when (error.Status == 403)
        {
            throw new ApiError(FailureKind.Permission, "You do not have permission for this action.", 403);
        }
        cancellationToken.ThrowIfCancellationRequested();
        // a sign-out while the request was in flight must not let a late success through
        if (await ActiveTokenAsync() is null)
            throw SignedOut();
        return result;
    }

    async Task<string?> ActiveTokenAsync()
    {
        var state = await authentication.GetAuthenticationStateAsync();
        var token = AccessTokenClaims.ActiveToken(state.User, timeProvider.GetUtcNow());
        if (token is null && state.User.Identity?.IsAuthenticated == true)
            logger.LogInformation("The signed-in user's access token has expired");
        return token;
    }

    static ApiError SignedOut() => new(FailureKind.Permission, "Sign in to continue.", 401);
}
