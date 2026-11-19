namespace TravelTracker.Api.Tests.Support;

/**
 * Collects every log line written through CapturingLoggerProvider, so tests can assert what surfaced.
 */
public sealed class LogSink
{
    private readonly List<LogEntry> entries = [];

    /** Every captured line, in write order. */
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (entries) return entries.ToList();
        }
    }

    /**
     * Stores one captured line.
     *
     * @param entry The line to store.
     */
    public void Add(LogEntry entry)
    {
        lock (entries) entries.Add(entry);
    }
}
