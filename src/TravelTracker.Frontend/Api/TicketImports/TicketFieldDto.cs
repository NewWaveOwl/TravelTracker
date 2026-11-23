namespace TravelTracker.Frontend.Api.TicketImports;

/**
 * One supported extracted field; null means the PDF did not provide a usable value.
 * @param Name Stable supported field name.
 * @param Value Explicit document value, or null.
 * @param Status Extracted, missing or uncertain; every value still needs review.
 */
public sealed record TicketFieldDto(string Name, string? Value, string Status);
