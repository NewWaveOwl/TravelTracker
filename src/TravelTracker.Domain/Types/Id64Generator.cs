namespace TravelTracker.Domain.Types;

/**
 * Issues sequential Id64 values in process. The first call reads the
 * highest stored key from the database (once, under a private async lock),
 * then every later call is an in-memory increment. The database stays a dumb
 * store: no state is persisted here, it is rebuilt from the data on restart.
 * Assumes a single backend instance; two instances would hand out the same
 * keys, so switch to a node-aware scheme (snowflake) before scaling out.
 *
 * @param readHighestKey Reads the highest key currently stored, or 0 when empty.
 */
public sealed class Id64Generator(Func<Task<long>> readHighestKey) {
    private readonly SemaphoreSlim seedLock = new(1, 1);
    private long last;
    private bool seeded;

    /** The next unused key. Safe to call concurrently; gaps after a crash are harmless. */
    public async ValueTask<Id64> NextAsync() {
        if (!Volatile.Read(ref seeded)) await SeedAsync();
        return new(Interlocked.Increment(ref last));
    }

    private async Task SeedAsync() {
        await seedLock.WaitAsync();
        try {
            if (seeded) return;
            last = await readHighestKey();
            Volatile.Write(ref seeded, true);
        } finally {
            seedLock.Release();
        }
    }
}
