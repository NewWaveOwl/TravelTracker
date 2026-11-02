using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Api.Tests;

/**
 * Captures rendered diagnostics so tests can check that sensitive details stay out.
 */
internal sealed class AuthenticationLogCapture : ILoggerProvider, ILogger
{
    internal ConcurrentQueue<string> Messages { get; } = new();
    internal ConcurrentQueue<Exception> Exceptions { get; } = new();

    /**
     * Supplies the same isolated log capture for a test's categories.
     * @param categoryName The logger category.
     * @return This test capture.
     */
    public ILogger CreateLogger(string categoryName) => this;
    public bool IsEnabled(LogLevel logLevel) => true;
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /**
     * Captures rendered diagnostics and any separate exception for disclosure checks.
     * @param logLevel The severity.
     * @param eventId The event identifier.
     * @param state The diagnostic state.
     * @param exception An optional exception.
     * @param formatter The configured diagnostic formatter.
     */
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Enqueue(formatter(state, exception));
        if (exception is not null) Exceptions.Enqueue(exception);
    }
    public void Dispose() { }
}
