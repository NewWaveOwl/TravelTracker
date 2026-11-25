namespace TravelTracker.Security.Authentication;

/**
 * Supplies a current bearer token for internal service-to-service calls to Users.
 */
public interface IServiceAccessTokenProvider
{
    /**
     * Returns a current bearer token, refreshing it from Supabase when configured and near expiry.
     *
     * @param cancellationToken Cancels any upstream token refresh.
     * @return A task containing a bearer token, or null when no credential is available.
     */
    Task<string?> GetTokenAsync(CancellationToken cancellationToken);
}
