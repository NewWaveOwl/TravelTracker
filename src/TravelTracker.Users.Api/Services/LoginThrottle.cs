namespace TravelTracker.Users.Api.Services;

/**
 * Locks a username after repeated failed sign-ins, so spreading guesses across addresses does not get around the per-address limit.
 * Unknown usernames count too, so a lockout reveals nothing about which accounts exist. State is in memory and per instance.
 * @param timeProvider Supplies the clock.
 */
public sealed class LoginThrottle(TimeProvider timeProvider)
{
    /** Failed attempts allowed inside the lock duration before the username locks. */
    public const int MaxFailures = 5;

    /** The window the failures are counted in, and so how long a username stays locked at most. */
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    // ponytail: usernames hash into a fixed table instead of a growing dictionary, so memory is bounded with no eviction or
    // capacity cliff. two usernames can very rarely share a bucket (the per-process hash seed hides which), so one can lock
    // the other; exact per-username state would need a shared store.
    private const int BucketCount = 1 << 16;

    private readonly object gate = new();
    private readonly Entry?[] buckets = new Entry?[BucketCount];

    /**
     * Reports whether a username is currently locked.
     * @param username The submitted username.
     * @return The time left on the lock, or null when sign-in may proceed.
     */
    public TimeSpan? RetryAfter(string username)
    {
        lock (gate)
        {
            if (buckets[Slot(username)] is not { } entry) return null;
            var remaining = entry.LockedUntil - timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /**
     * Counts a failed sign-in. The username locks for the lock duration once it has MaxFailures failures inside that window.
     * @param username The submitted username.
     */
    public void RecordFailure(string username)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var entry = buckets[Slot(username)] ??= new Entry();
            while (entry.Failures.Count > 0 && now - entry.Failures.Peek() >= LockDuration) entry.Failures.Dequeue();
            entry.Failures.Enqueue(now);
            if (entry.Failures.Count > MaxFailures) entry.Failures.Dequeue();
            if (entry.Failures.Count == MaxFailures) entry.LockedUntil = now + LockDuration;
        }
    }

    /**
     * Forgets a username's failures after a successful sign-in.
     * @param username The submitted username.
     */
    public void Reset(string username)
    {
        lock (gate)
            buckets[Slot(username)] = null;
    }

    private static int Slot(string username) => (username.Trim().ToUpperInvariant().GetHashCode() & int.MaxValue) % BucketCount;

    private sealed class Entry
    {
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset LockedUntil;
    }
}
