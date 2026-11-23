namespace TravelTracker.Frontend.Api.TicketImports;

/**
 * Temporary owner-scoped ticket extraction, independent of the saved itinerary.
 * @param ImportId Opaque temporary import identifier.
 * @param TripId Existing owned trip the import belongs to.
 * @param FileName Sanitized display name.
 * @param ExpiresAt Time after which the private source document is unavailable.
 * @param Fields Supported proposed values, never an automatically saved trip.
 */
public sealed record TicketImportDto(Guid ImportId, Guid TripId, string FileName, DateTimeOffset ExpiresAt, TicketFieldDto[] Fields);
