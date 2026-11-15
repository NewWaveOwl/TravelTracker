using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data;

/** Issues internal keys from the current database while the caller holds the provisioning lock. */
internal sealed class EntityIdGenerator
{
    /** Global advisory lock key shared by every writer that provisions, bootstraps or changes roles; all must use this same value. */
    internal const long ProvisioningLock = 0x5454555345520001;

    /**
     * Allocates the next app-owned internal key from the persisted high-water mark.
     * @param database A context whose transaction holds the Users provisioning lock.
     * @param cancellationToken Cancels the lookup.
     * @return The next Data-only entity key.
     */
    public async ValueTask<Id64> NextAsync(UsersDbContext database, CancellationToken cancellationToken)
    {
        var lastId = await database.Accounts.Select(record => (long?)record.Id).MaxAsync(cancellationToken) ?? 0;
        return new Id64(checked(lastId + 1));
    }
}
