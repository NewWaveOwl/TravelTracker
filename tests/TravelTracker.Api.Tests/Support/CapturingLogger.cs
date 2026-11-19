using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests.Support;

/**
 * Writes each enabled log call into a LogSink.
 *
 * @param sink Receives the captured lines.
 * @param category The category this logger was created for.
 */
internal sealed class CapturingLogger(LogSink sink, string category) : ILogger
{
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
