using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Users.Tests.Support;

/** Captures formatted messages and structured log state for disclosure assertions. */
public sealed class UsersLogCapture : ILoggerProvider
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Entries);

    public void Dispose() { }

    sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(formatter(state, exception));
            if (exception is not null) entries.Enqueue(exception.ToString());
            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
                foreach (var property in properties)
                    entries.Enqueue(property.Value?.ToString() ?? "");
        }
    }
}
