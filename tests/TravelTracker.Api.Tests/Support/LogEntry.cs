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
