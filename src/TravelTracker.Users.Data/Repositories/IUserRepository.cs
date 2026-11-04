using TravelTracker.Users.Domain.Models;

namespace TravelTracker.Users.Data.Repositories;

/** Persists account lookup and the atomic one-time administrator setup. */
public interface IUserRepository
{
    /**
     * Resolves only an exact provider identity.
     * @param issuer Validated issuer.
     * @param subject Validated subject.
     * @param cancellationToken Request cancellation.
     * @return Account or null if this identity has no local account.
     */
    Task<UserAccount?> FindAsync(string issuer, string subject, CancellationToken cancellationToken);

    /**
     * Atomically creates the first administrator and permanent completion/audit rows.
     * @param issuer Validated issuer, approved by the bootstrap application workflow.
     * @param subject Validated subject bound to the verified profile.
     * @param email Verified allowed email.
     * @param cancellationToken Request cancellation.
     * @return Saved account, or the original account on exact-identity replay without re-elevation; null when setup is consumed by another identity or prohibited.
     */
    Task<UserAccount?> BootstrapAsync(string issuer, string subject, string email, CancellationToken cancellationToken);

    /**
     * Provisions a verified identity as an active traveller without replacing saved roles or standing.
     * @param issuer Validated issuer.
     * @param subject Validated subject.
     * @param email Verified provider email.
     * @param cancellationToken Request cancellation.
     * @return The existing or newly provisioned account.
     */
    Task<UserAccount> GetOrCreateTravellerAsync(string issuer, string subject, string email, CancellationToken cancellationToken);

    /**
     * Lists local accounts only for a currently active administrator.
     * @param issuer Validated actor issuer.
     * @param subject Validated actor subject.
     * @param cancellationToken Request cancellation.
     * @return Ordered accounts, or null when the actor is not an active administrator.
     */
    Task<IReadOnlyList<UserAccount>?> ListForAdministratorAsync(string issuer, string subject, CancellationToken cancellationToken);

    /**
     * Applies one administrator-authorized role transition with last-admin protection and a durable audit.
     * @param actorIssuer Validated actor issuer.
     * @param actorSubject Validated actor subject.
     * @param targetId Public account identifier being changed.
     * @param role Desired exact role.
     * @param expectedRole Role shown when the edit began.
     * @param reason Required written reason, at most 500 characters after trimming.
     * @param cancellationToken Request cancellation.
     * @return A categorized outcome, with the saved account only for a successful change.
     */
    Task<UserRoleChangeResult> ChangeRoleAsync(string actorIssuer, string actorSubject, Guid targetId, string role, string expectedRole, string reason, CancellationToken cancellationToken);
}
