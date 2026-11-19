using Microsoft.Extensions.Logging;

namespace TravelTracker.Frontend.Tests.Support;

/**
 * keeps every entry written to an ILogger<T> so tests can assert what was logged and with which exception.
 *
 * @param category The category the entries are recorded under.
 */
public sealed class RecordingLogger<T> : ILogger<T>
{
    readonly List<LogEntry> entries = [];

    /** every entry written so far, in order. */
    public IReadOnlyList<LogEntry> Entries => entries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));

    /**
     * One recorded log entry.
     *
     * @param level The severity the entry was written at.
     * @param exception The exception passed with the entry, if any.
     * @param message The formatted message.
     */
    public sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);
}
