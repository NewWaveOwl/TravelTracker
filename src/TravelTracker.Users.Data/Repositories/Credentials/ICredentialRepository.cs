using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories.Credentials;

/**
 * Stores and reads password sign-in credentials alongside the account they belong to.
 */
public interface ICredentialRepository
{
    /**
     * Creates an active traveller that can sign in with the given login.
     * @param issuer Issuer recorded on the new account.
     * @param login Sign-in name, compared case-insensitively.
     * @param passwordHash Already-hashed password.
     * @param createdAt The application time for the new account.
     * @param cancellationToken Cancels the save.
     * @return The new account, or null when the login is already registered.
     */
    Task<UserAccount?> RegisterAsync(string issuer, string login, string passwordHash, DateTimeOffset createdAt, CancellationToken cancellationToken);

    /**
     * Finds the account and stored password hash for a login.
     * @param login Sign-in name, compared case-insensitively.
     * @param cancellationToken Cancels the lookup.
     * @return The account and hash, or null when no password account has that login.
     */
    Task<StoredCredential?> FindAsync(string login, CancellationToken cancellationToken);

    /**
     * Replaces the hash when the hasher asks for a stronger one.
     * @param publicId The account whose hash changes.
     * @param passwordHash The new hash.
     * @param cancellationToken Cancels the save.
     * @return A task that completes when the hash is saved.
     */
    Task UpdateHashAsync(Guid publicId, string passwordHash, CancellationToken cancellationToken);
}
