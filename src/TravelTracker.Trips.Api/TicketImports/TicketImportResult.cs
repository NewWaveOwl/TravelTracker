namespace TravelTracker.Trips.Api.TicketImports;

/**
 * Maps one import operation to a safe HTTP outcome without exposing parser exceptions.
 * @param StatusCode Documented HTTP outcome.
 * @param Value Successful result, or default when refused.
 * @param Error Safe explanation with no document content or identity values.
 */
public sealed record TicketImportResult<T>(int StatusCode, T? Value = default, string? Error = null);
