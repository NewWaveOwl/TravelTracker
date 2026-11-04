using TravelTracker.Attractions.Data.SeedData;
using TravelTracker.Attractions.Domain.Types;

namespace TravelTracker.Attractions.Data;

/**
 * Issues new entity identities in process: a sequential key plus a uuidv7.
 * The first call reads the highest stored key (once, under a private async
 * lock), then every later call is an in-memory increment. The database stays
 * a dumb store: no counter is persisted, it is rebuilt from the data on restart.
 * One counter serves every table, so keys stay unique across entities.
 * Assumes a single backend instance; two instances would hand out the same
 * keys, so switch to a node-aware scheme (snowflake) before scaling out.
 *
 * @param readHighestKey Reads the highest key currently stored, or 0 when empty.
 */
public sealed class EntityIdGenerator(Func<CancellationToken, Task<long>> readHighestKey) {
    private readonly SemaphoreSlim seedLock = new(1, 1);
    private long last;
    private bool seeded;

    /**
     * Creates a generator for the in-memory repositories, starting after the highest seeded key.
     *
     * @return A generator that never reuses a seeded key.
     */
    public static EntityIdGenerator ForSeedData() => new(_ => Task.FromResult(new[] {
        AttractionSeedData.Create().Max(record => (long)record.AttractionId.Key),
    }.Max()));

    /**
     * Issues the next identity. Safe to call concurrently; gaps after a crash are harmless.
     * @param cancellationToken Cancels initial database lookup or lock acquisition.
     *
     * @return A task containing a fresh identity for the entity type.
     */
    public async ValueTask<EntityId<TEntity>> NextAsync<TEntity>(CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Volatile.Read(ref seeded)) await SeedAsync(cancellationToken);
        return new(new Id64(Interlocked.Increment(ref last)), Guid.CreateVersion7());
    }

    private async Task SeedAsync(CancellationToken cancellationToken) {
        await seedLock.WaitAsync(cancellationToken);
        try {
            if (seeded) return;
            last = await readHighestKey(cancellationToken);
            Volatile.Write(ref seeded, true);
        } finally {
            seedLock.Release();
        }
    }
}
