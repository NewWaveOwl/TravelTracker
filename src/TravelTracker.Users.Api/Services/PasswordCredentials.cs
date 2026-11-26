using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace TravelTracker.Users.Api.Services;

/**
 * The one password rule and hashing setup shared by registration, sign-in and password changes.
 */
public static class PasswordCredentials
{
    /** Shortest accepted password, counted in characters. */
    public const int MinimumLength = 6;

    /** Longest accepted password, counted in characters. */
    public const int MaximumLength = 128;

    /** Work factor for new hashes; hashes stored with a lower count are upgraded at the next sign-in. */
    public const int Iterations = 1 << 18;

    private static readonly PasswordHasher<object> Hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = Iterations }));

    /**
     * Checks a candidate password against the length rule.
     * @param password The password the caller supplied.
     * @return The copy to show the caller, or null when the password is acceptable.
     */
    public static string? Validate(string password) =>
        password.Length < MinimumLength || password.Length > MaximumLength
            ? $"Use {MinimumLength} to {MaximumLength} characters."
            : null;

    /**
     * Hashes a password for storage.
     * @param password The plain password; never stored or logged.
     * @return The PBKDF2 hash to persist.
     */
    public static string Hash(string password) => Hasher.HashPassword(new object(), password);

    /**
     * Compares a plain password with a stored hash.
     * @param hash The stored hash.
     * @param password The plain password to check.
     * @return The hasher's verdict, including whether the hash should be upgraded.
     */
    public static PasswordVerificationResult Verify(string hash, string password) =>
        Hasher.VerifyHashedPassword(new object(), hash, password);
}
