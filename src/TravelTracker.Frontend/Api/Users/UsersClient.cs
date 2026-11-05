namespace TravelTracker.Frontend.Api.Users;

/**
 * Reads live Users roles and completes an operator-configured, one-time bootstrap.
 * @param client Uses only the authenticated session and a configured destination.
 * @param configuration Holds the setup secret on the server.
 */
public sealed class UsersClient(ProtectedClient client, IConfiguration configuration)
{
    public bool SetupEnabled => configuration["AdminBootstrap:Secret"] is { Length: >= 32 };

    /**
     * Reads current server-owned standing without treating a token role as application authority.
     * @param cancellationToken Cancels the consuming view.
     * @return A profile, or null until that identity has a Users account.
     */
    public async Task<SelfUserProfile?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        try { return await client.GetJsonAsync<SelfUserProfile>("Users", "/api/v1/users/me", cancellationToken); }
        catch (ApiError failure) when (failure.Status == 404) { return null; }
    }

    /**
     * Lists application accounts after the service checks the current administrator's standing.
     * @param cancellationToken Cancels a closed administrator panel.
     * @return The service-ordered account list; no provider directory credentials or identities.
     */
    public async Task<IReadOnlyList<UserProfile>> ListAsync(CancellationToken cancellationToken = default) =>
        await client.GetJsonAsync<List<UserProfile>>("Users", "/api/v1/users", cancellationToken)
        ?? throw new ApiError(FailureKind.Unexpected, "The users list could not be read. Refresh and try again.");

    /**
     * Requests a role change with an explicit reason and the version of the role the administrator reviewed.
     * @param id Public target user identifier.
     * @param role Requested application role.
     * @param expectedRole Last observed target role, checked atomically by the service.
     * @param reason Explanation retained in the permanent role audit.
     * @param cancellationToken Cancels the pending request.
     * @return Only the server-confirmed updated profile.
     */
    public async Task<UserProfile> ChangeRoleAsync(Guid id, string role, string expectedRole, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.SendJsonAsync<UserProfile>("Users", HttpMethod.Patch, $"/api/v1/users/{id:D}/role", new { role, expectedRole, reason }, cancellationToken)
                ?? throw new ApiError(FailureKind.Unexpected, "The role change could not be confirmed. Refresh users before retrying.");
        }
        catch (ApiError failure) when (failure.Status is 400 or 404 or 409)
        {
            var message = failure.Status switch
            {
                400 => "Choose a valid role and enter a reason of 1–500 characters. Inactive accounts cannot be promoted.",
                404 => "That user is no longer available. Refresh the users list.",
                _ => "The role change was refused. Refresh users before retrying; the last active administrator cannot be demoted."
            };
            throw new ApiError(failure.Kind, message, failure.Status, failure.Errors);
        }
    }

    /**
     * Applies bootstrap only after the API independently verifies identity, allowed email and one-time state.
     * @param cancellationToken Cancels the view.
     * @return The confirmed profile, never an optimistic client-side role.
     */
    public async Task<UserProfile> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        if (!SetupEnabled) throw new ApiError(FailureKind.Permission, "First-administrator setup is not enabled.", 403);
        return await client.BootstrapFirstAdministratorAsync<UserProfile>(configuration["AdminBootstrap:Secret"]!, cancellationToken)
            ?? throw new ApiError(FailureKind.Unexpected, "Administrator setup could not be confirmed. Reload your account before retrying.");
    }
}
