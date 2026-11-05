using Microsoft.EntityFrameworkCore;
using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data;

/**
 * Issues persistence keys above the highest key already stored by the single Users writer.
 */
internal sealed class EntityIdGenerator
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    private static long lastId;
    private static bool initialized;

    /**
     * Allocates the next internal key after initializing from persisted accounts once per process.
     * @param db The Users context used to read the persisted high-water mark.
     * @param cancellationToken Cancels initialization.
     * @return The next Data-only entity key.
     */
    public async ValueTask<Id64> NextAsync(UsersDbContext db, CancellationToken cancellationToken)
    {
        if (!Volatile.Read(ref initialized))
        {
            await InitializationGate.WaitAsync(cancellationToken);
            try
            {
                if (!initialized)
                {
                    lastId = await db.Accounts.Select(record => (long?)record.Id).MaxAsync(cancellationToken) ?? 0;
                    Volatile.Write(ref initialized, true);
                }
            }
            finally
            {
                InitializationGate.Release();
            }
        }

        return new Id64(Interlocked.Increment(ref lastId));
    }
}
