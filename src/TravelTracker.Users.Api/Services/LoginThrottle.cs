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

    private const int MaxTrackedUsernames = 10_000;

    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = new();

    /**
     * Reports whether a username is currently locked.
     * @param username The submitted username.
     * @return The time left on the lock, or null when sign-in may proceed.
     */
    public TimeSpan? RetryAfter(string username)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(Key(username), out var entry)) return null;
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
            var key = Key(username);
            if (!entries.TryGetValue(key, out var entry))
            {
                if (entries.Count >= MaxTrackedUsernames) MakeRoom(now);
                // ponytail: only if every tracked username is locked, so a new one goes uncounted; the per-address limit still applies
                if (entries.Count >= MaxTrackedUsernames) return;
                entries[key] = entry = new Entry();
            }
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
            entries.Remove(Key(username));
    }

    // drops expired entries, then the quietest unlocked one if that was not enough; locked usernames are never evicted
    private void MakeRoom(DateTimeOffset now)
    {
        string? quietest = null;
        var quietestFailure = DateTimeOffset.MaxValue;
        foreach (var (key, entry) in entries.ToList())
        {
            var lastFailure = entry.Failures.Count > 0 ? entry.Failures.Last() : DateTimeOffset.MinValue;
            if (entry.LockedUntil > now) continue;
            if (now - lastFailure >= LockDuration) entries.Remove(key);
            else if (lastFailure < quietestFailure) (quietest, quietestFailure) = (key, lastFailure);
        }
        if (entries.Count >= MaxTrackedUsernames && quietest is not null) entries.Remove(quietest);
    }

    private static string Key(string username) => username.Trim().ToUpperInvariant();

    private sealed class Entry
    {
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset LockedUntil;
    }
}
