namespace TravelTracker.Trips.Api.Contracts;

/**
 * Proposes one supported value without claiming that the ticket has been verified.
 * @param Name Stable name of a supported ticket field.
 * @param Value Explicitly extracted value, or null when missing or ambiguous.
 * @param Status extracted, missing or uncertain; every value still requires traveller review.
 */
public sealed record TicketImportFieldResponse(string Name, string? Value, string Status);
