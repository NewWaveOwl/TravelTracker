namespace TravelTracker.Frontend.Api.Users;

/**
 * Reads and updates the signed-in caller's profile, reads live Users roles, and completes an operator-configured, one-time bootstrap.
 * @param client Uses only the authenticated session and a configured destination.
 * @param configuration Holds the setup secret on the server.
 * @param logger Records expected outcomes such as a missing account; optional so tests can omit it.
 */
public sealed class UsersClient(ProtectedClient client, IConfiguration configuration, ILogger<UsersClient>? logger = null)
{
    public const int MaxPictureUploadBytes = 1024 * 1024;
    public const int MaxPictureReadBytes = 2 * 1024 * 1024;
    public const int MaxPictureSide = 512;
    const string PicturePath = "/api/v1/users/me/picture";

    /**
     * Reads the current owner's private normalized PNG through bearer-authenticated HTTP.
     * @param expectedSessionId Optional rendered-owner session guard.
     * @param cancellationToken Cancels a closed or superseded view.
     * @return The bounded image, or null when no picture has been saved.
     */
    public async Task<byte[]?> GetPictureAsync(string? expectedSessionId = null, CancellationToken cancellationToken = default)
    {
        try { return await client.SendImageAsync("Users", HttpMethod.Get, PicturePath, null, null, MaxPictureReadBytes, expectedSessionId, cancellationToken); }
        catch (ApiError failure) when (failure.Status == 404) { return null; }
    }

    /**
     * Replaces only the current active owner's photo using raw PNG/JPEG bytes; no multipart filename is sent.
     * @param picture Bounded input bytes. Known oversized header dimensions are rejected early; the server verifies decoded pixels and static format.
     * @param contentType The file's PNG or JPEG media type.
     * @param expectedSessionId Optional rendered-owner session guard.
     * @param cancellationToken Cancels a closed or superseded view.
     * @return Completion only after the API confirms 204. A subsequent read always uses the fixed own-picture path.
     */
    public async Task ReplacePictureAsync(byte[] picture, string contentType, string? expectedSessionId = null, CancellationToken cancellationToken = default)
    {
        if (picture.Length is < 1 or > MaxPictureUploadBytes)
            throw new ApiError(FailureKind.Validation, "Choose a photo no larger than 1 MiB (1,048,576 bytes).", 413);
        if (!string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase) && !string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
            throw new ApiError(FailureKind.Validation, "Choose one static PNG or JPEG photo.", 415);
        if (PictureDimensions.ExceedsLimit(picture, contentType, MaxPictureSide))
            throw new ApiError(FailureKind.Validation, "Choose a photo with width and height no greater than 512 pixels. Resize the photo before selecting it again.", 413);
        try
        {
            await client.SendImageAsync("Users", HttpMethod.Put, PicturePath, picture, contentType.ToLowerInvariant(), MaxPictureReadBytes, expectedSessionId, cancellationToken);
        }
        catch (ApiError failure) when (failure.Status is 400 or 413 or 415)
        {
            throw new ApiError(FailureKind.Validation, failure.Status switch
            {
                413 => "Choose a photo no larger than 1 MiB with width and height no greater than 512 pixels.",
                415 => "Choose one static PNG or JPEG photo with its matching format.",
                _ => "The photo is empty or could not be read. Choose a valid static PNG or JPEG."
            }, failure.Status);
        }
    }

    /** true when the operator provisioned a long enough bootstrap secret. */
    public bool SetupEnabled => configuration["AdminBootstrap:Secret"] is { Length: >= 32 };

    /**
     * Reads current server-owned standing without treating a token role as application authority.
     * @param cancellationToken Cancels the consuming view.
     * @return A profile, or null until that identity has a Users account.
     */
    public async Task<SelfUserProfile?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.GetJsonAsync<SelfUserProfile>("Users", "/api/v1/users/me", cancellationToken);
        }
        catch (ApiError failure) when (failure.Status == 404)
        {
            // expected for a first sign-in: the identity has no Users account yet
            logger?.LogInformation("The signed-in identity has no Users account yet");
            return null;
        }
    }

    /**
     * Updates only changed own-profile fields and returns the server-confirmed representation.
     * @param current The latest server-confirmed profile used to omit unchanged values.
     * @param update The caller-editable address value.
     * @param cancellationToken Cancels the pending request.
     * @return The updated profile returned by the Users API.
     */
    public async Task<SelfUserProfile> UpdateMeAsync(SelfUserProfile current, UpdateSelfProfileRequest update, CancellationToken cancellationToken = default)
    {
        var fields = update.ChangedFields(current);
        if (fields.Count == 0) return current;
        return await client.SendJsonAsync<SelfUserProfile>("Users", HttpMethod.Patch, "/api/v1/users/me", fields, cancellationToken)
            ?? throw new ApiError(FailureKind.Unexpected, "The Users service did not confirm the profile update.");
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
     * @param identifier Public target user identifier.
     * @param role Requested application role.
     * @param expectedRole Last observed target role, checked atomically by the service.
     * @param reason Explanation retained in the permanent role audit.
     * @param cancellationToken Cancels the pending request.
     * @return Only the server-confirmed updated profile.
     */
    public async Task<UserProfile> ChangeRoleAsync(Guid identifier, string role, string expectedRole, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            return await client.SendJsonAsync<UserProfile>("Users", HttpMethod.Patch, $"/api/v1/users/{identifier:D}/role", new { role, expectedRole, reason }, cancellationToken)
                ?? throw new ApiError(FailureKind.Unexpected, "The role change could not be confirmed. Refresh users before retrying.");
        }
        catch (ApiError failure) when (failure.Status is 400 or 404 or 409)
        {
            var message = failure.Status switch
            {
                400 => "Choose a valid role and enter a reason of 1 to 500 characters. Inactive accounts cannot be promoted.",
                404 => "That user is no longer available. Refresh the users list.",
                _ => "The role change was refused, for example because the account changed or it is the last active administrator. Refresh users before retrying."
            };
            throw new ApiError(failure.Kind, message, failure.Status, failure.Errors);
        }
    }

    /**
     * Applies bootstrap only after the API independently verifies identity, allowed username and one-time state.
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
