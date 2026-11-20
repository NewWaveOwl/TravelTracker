using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Api.Contracts;

/** Public account response, omitting provider subject, issuer, credentials and persistence internals. */
public sealed record UserProfileResponse(Guid UserId, string? Username, string Role, string Status)
{
    /**
     * Maps an account to its public admin-facing shape.
     *
     * @param account The account to expose.
     * @return The public response; Username is the sign-in name.
     */
    public static UserProfileResponse From(UserAccount account) => new(account.PublicId, account.Username,
        account.Role == UserRole.User ? "traveller" : "administrator", account.Status.ToString().ToLowerInvariant());
}
