using TravelTracker.Users.Domain.Models;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Repositories.Profile;

/**
 * Resolves Users profiles and persists profile-only updates.
 */
public interface IUserRepository
{
    /**
     * Finds an account by its validated provider identity.
     * @param identity The identity-provider issuer and subject.
     * @param cancellationToken Cancels the lookup.
     * @return The account or null when no local profile exists.
     */
    Task<UserAccount?> FindByProviderIdentityAsync(ProviderIdentity identity, CancellationToken cancellationToken);

    /**
     * Finds an account by its public UUID.
     * @param publicId The public account identifier.
     * @param cancellationToken Cancels the lookup.
     * @return The account or null when no local profile exists.
     */
    Task<UserAccount?> FindByPublicIdAsync(Guid publicId, CancellationToken cancellationToken);

    /**
     * Returns an existing account or provisions a default active traveller.
     * @param identity The validated identity-provider issuer and subject.
     * @param createdAt The application time for a newly provisioned account.
     * @param cancellationToken Cancels the lookup or save.
     * @return The existing or newly persisted account.
     */
    Task<UserAccount> GetOrCreateAsync(ProviderIdentity identity, DateTimeOffset createdAt, CancellationToken cancellationToken);

    /**
     * Updates supplied profile fields on an active account without changing server-owned state.
     * @param identity The validated owner identity.
     * @param username The replacement username, or null to preserve it.
     * @param email The replacement email, or null to preserve it.
     * @param address The replacement address, or null to clear it when supplied.
     * @param addressSpecified Whether the address field was supplied.
     * @param updatedAt The application time for this update.
     * @param cancellationToken Cancels the read or save.
     * @return Saved profile, unchanged inactive account, or null when absent.
     * @throws DbUpdateConcurrencyException when a concurrent update changed the loaded record.
     */
    Task<UserAccount?> UpdateProfileAsync(
        ProviderIdentity identity,
        string? username,
        string? email,
        string? address,
        bool addressSpecified,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}
