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
    private readonly Dictionary<string, Queue<DateTimeOffset>> entries = new();

    // shared by every username that shows up once the tracker is full, so the cap cannot be used to dodge the lockout
    private readonly Queue<DateTimeOffset> overflow = new();

    /**
     * Reports whether a username is currently locked.
     * @param username The submitted username.
     * @return The time left on the lock, or null when sign-in may proceed.
     */
    public TimeSpan? RetryAfter(string username)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var failures = entries.TryGetValue(Key(username), out var entry) ? entry : entries.Count >= MaxTrackedUsernames ? overflow : null;
            if (failures is null) return null;
            Expire(failures, now);
            return failures.Count < MaxFailures ? null : failures.Peek() + LockDuration - now;
        }
    }

    /**
     * Counts a failed sign-in. The username locks once it has MaxFailures failures inside the lock duration.
     * @param username The submitted username.
     */
    public void RecordFailure(string username)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            var key = Key(username);
            if (entries.Count >= MaxTrackedUsernames) Prune(now);
            // ponytail: past the cap unseen usernames share one counter; a shared store would track them separately
            if (!entries.TryGetValue(key, out var failures))
            {
                if (entries.Count >= MaxTrackedUsernames) failures = overflow;
                else entries[key] = failures = new Queue<DateTimeOffset>();
            }
            Expire(failures, now);
            failures.Enqueue(now);
            if (failures.Count > MaxFailures) failures.Dequeue();
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

    private static void Expire(Queue<DateTimeOffset> failures, DateTimeOffset now)
    {
        while (failures.Count > 0 && now - failures.Peek() >= LockDuration) failures.Dequeue();
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in entries.Keys.ToList())
        {
            Expire(entries[key], now);
            if (entries[key].Count == 0) entries.Remove(key);
        }
    }

    private static string Key(string username) => username.Trim().ToUpperInvariant();
}
