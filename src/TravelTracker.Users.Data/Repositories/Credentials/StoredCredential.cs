using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories.Credentials;

/**
 * An account paired with its stored password hash.
 * @param Account The account the credential signs in.
 * @param PasswordHash The stored hash, never exposed past the sign-in check.
 */
public sealed record StoredCredential(UserAccount Account, string PasswordHash);
