using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests.Support;

/**
 * One captured log line.
 *
 * @param Level The severity the code logged at.
 * @param Category The logger category, usually the full type name.
 * @param Message The formatted message.
 */
public sealed record LogEntry(LogLevel Level, string Category, string Message);

/**
 * Collects every log line written through CapturingLoggerProvider, so tests can assert what surfaced.
 */
public sealed class LogSink {
    private readonly List<LogEntry> entries = [];

    /** Every captured line, in write order. */
    public IReadOnlyList<LogEntry> Entries {
        get {
            lock (entries) return entries.ToList();
        }
    }

    /**
     * Stores one captured line.
     *
     * @param entry The line to store.
     */
    public void Add(LogEntry entry) {
        lock (entries) entries.Add(entry);
    }
}

/**
 * Routes every logger created through the DI container into a LogSink.
 *
 * @param sink Receives the captured lines.
 */
public sealed class CapturingLoggerProvider(LogSink sink) : ILoggerProvider {
    /**
     * Creates a logger for one category.
     *
     * @param categoryName The category the framework requests.
     * @return A logger that writes into the sink.
     */
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink, categoryName);

    /** Nothing to release; the sink outlives the provider. */
    public void Dispose() {
    }
}

/**
 * Writes each enabled log call into a LogSink.
 *
 * @param sink Receives the captured lines.
 * @param category The category this logger was created for.
 */
internal sealed class CapturingLogger(LogSink sink, string category) : ILogger {
    /**
     * Scopes are not used by the trip code, so none are created.
     *
     * @param state Ignored.
     * @return Null.
     */
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /**
     * Every level is captured, so no message is dropped by the test sink.
     *
     * @param logLevel The level being checked.
     * @return Always true.
     */
    public bool IsEnabled(LogLevel logLevel) => true;

    /**
     * Captures one formatted log line.
     *
     * @param logLevel The severity.
     * @param eventId Ignored.
     * @param state The message state.
     * @param exception Any attached exception; its text is not captured.
     * @param formatter Produces the message text.
     */
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        sink.Add(new(logLevel, category, formatter(state, exception)));
}
