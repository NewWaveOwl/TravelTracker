using System.Collections.Concurrent;

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

    /** How long a username stays locked, and how long a failure keeps counting. */
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private const int MaxTrackedUsernames = 10_000;

    private readonly ConcurrentDictionary<string, Entry> entries = new();

    /**
     * Reports whether a username is currently locked.
     * @param username The submitted username.
     * @return The time left on the lock, or null when sign-in may proceed.
     */
    public TimeSpan? RetryAfter(string username)
    {
        if (!entries.TryGetValue(Key(username), out var entry)) return null;
        lock (entry)
        {
            var remaining = entry.LockedUntil - timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    /**
     * Counts a failed sign-in and locks the username once it reaches the limit.
     * @param username The submitted username.
     */
    public void RecordFailure(string username)
    {
        var now = timeProvider.GetUtcNow();
        var key = Key(username);
        if (entries.Count >= MaxTrackedUsernames) Prune(now);
        // ponytail: past the cap new usernames go untracked; the per-address limit still applies, move to a shared store if it matters
        if (entries.Count >= MaxTrackedUsernames && !entries.ContainsKey(key)) return;
        var entry = entries.GetOrAdd(key, _ => new Entry());
        lock (entry)
        {
            if (now - entry.LastFailure >= LockDuration) entry.Failures = 0;
            entry.LastFailure = now;
            if (++entry.Failures >= MaxFailures) entry.LockedUntil = now + LockDuration;
        }
    }

    /**
     * Forgets a username's failures after a successful sign-in.
     * @param username The submitted username.
     */
    public void Reset(string username) => entries.TryRemove(Key(username), out _);

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in entries)
            if (now - pair.Value.LastFailure >= LockDuration) entries.TryRemove(pair.Key, out _);
    }

    private static string Key(string username) => username.Trim().ToUpperInvariant();

    private sealed class Entry
    {
        public int Failures;
        public DateTimeOffset LastFailure;
        public DateTimeOffset LockedUntil;
    }
}
