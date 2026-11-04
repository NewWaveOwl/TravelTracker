using System.Security.Cryptography;
using System.Text;

namespace TravelTracker.Users.Api.Services;

/** Constant-time verification for operator-owned bootstrap and service credentials. */
public static class SecretVerifier
{
    /**
     * Checks one opaque credential without logging or exposing it.
     * @param configured The server-only configured secret.
     * @param supplied The credential received through the intended header.
     * @return True only for a configured secret of at least 32 characters and an exact match.
     */
    public static bool Matches(string? configured, string? supplied) => configured is { Length: >= 32 } && supplied is not null &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(configured)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied)));
}
