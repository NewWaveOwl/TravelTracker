using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Api.Contracts;

/** Public account response, omitting provider subject, issuer, credentials and persistence internals. */
public sealed record UserProfileResponse(Guid UserId, string? Email, string Role, string Status)
{
    /**
     * Maps an account to its public admin-facing shape.
     *
     * @param account The account to expose.
     * @return The public response; Email carries the verified provider email.
     */
    public static UserProfileResponse From(UserAccount account) => new(account.PublicId, account.VerifiedEmail,
        account.Role == UserRole.User ? "traveller" : "administrator", account.Status.ToString().ToLowerInvariant());
}
