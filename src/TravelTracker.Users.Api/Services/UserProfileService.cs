using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Api.Contracts;
using TravelTracker.Users.Data.Repositories.Profile;
using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Api.Services;

/**
 * Coordinates authenticated self-profile reads and updates.
 * @param profiles Resolves and persists the caller's account profile.
 */
public sealed class UserProfileService(IUserRepository profiles)
{
    /**
     * Reads or provisions the caller's account and applies standing rules.
     * @param identity The caller's validated provider identity.
     * @param cancellationToken Cancels the lookup or provisioning operation.
     * @return The caller's profile or a forbidden outcome for a deactivated account.
     */
    public async Task<UserProfileResult> ReadOwnProfileAsync(
        ProviderIdentity identity,
        CancellationToken cancellationToken)
    {
        var account = await profiles.GetOrCreateAsync(identity, DateTimeOffset.UtcNow, cancellationToken);
        return account.Status == UserStatus.Deactivated
            ? new UserProfileResult(UserProfileResultKind.Forbidden)
            : new UserProfileResult(UserProfileResultKind.Success, account);
    }

    /**
     * Validates and updates only supplied fields on the active caller's account.
     * @param identity The caller's validated provider identity.
     * @param request The whitelisted profile changes.
     * @param cancellationToken Cancels the lookup or save.
     * @return The saved profile or a validation, standing or concurrency outcome.
     */
    public async Task<UserProfileResult> UpdateOwnProfileAsync(
        ProviderIdentity identity,
        UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
    {
        var account = await profiles.FindByProviderIdentityAsync(identity, cancellationToken);
        if (account?.Status != UserStatus.Active)
        {
            return new UserProfileResult(UserProfileResultKind.Forbidden);
        }

        var username = request.Username?.Trim();
        var email = request.Email?.Trim();
        var address = request.Address?.Trim();
        var candidate = account with
        {
            Username = request.HasUsername ? username : account.Username,
            Email = request.HasEmail ? email : account.Email,
            Address = request.HasAddress ? address : account.Address
        };
        var errors = candidate.Validate();
        if (!request.HasUsername && !request.HasEmail && !request.HasAddress)
        {
            errors["profile"] = ["Supply at least one editable field."];
        }
        if (request.HasUsername && username is null)
        {
            errors["username"] = ["Username cannot be null."];
        }
        if (request.HasEmail && email is null)
        {
            errors["email"] = ["Email cannot be null."];
        }
        if (errors.Count != 0)
        {
            return new UserProfileResult(UserProfileResultKind.ValidationFailure, ValidationErrors: errors);
        }

        try
        {
            var updated = await profiles.UpdateProfileAsync(identity, username, email, address,
                request.HasAddress, DateTimeOffset.UtcNow, cancellationToken);
            return updated?.Status == UserStatus.Active
                ? new UserProfileResult(UserProfileResultKind.Success, updated)
                : new UserProfileResult(UserProfileResultKind.Forbidden);
        }
        catch (UserProfileUsernameConflictException)
        {
            return new UserProfileResult(UserProfileResultKind.UsernameConflict);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new UserProfileResult(UserProfileResultKind.ConcurrencyConflict);
        }
    }
}
