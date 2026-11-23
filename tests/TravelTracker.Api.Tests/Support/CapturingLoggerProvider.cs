using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests.Support;

/**
 * Routes every logger created through the DI container into a LogSink.
 *
 * @param sink Receives the captured lines.
 */
public sealed class CapturingLoggerProvider(LogSink sink) : ILoggerProvider
{
    /**
     * Creates a logger for one category.
     *
     * @param categoryName The category the framework requests.
     * @return A logger that writes into the sink.
     */
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(sink, categoryName);

    /** Nothing to release; the sink outlives the provider. */
    public void Dispose()
    {
    }
}
