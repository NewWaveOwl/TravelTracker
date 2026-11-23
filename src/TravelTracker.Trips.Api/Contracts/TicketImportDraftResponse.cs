namespace TravelTracker.Trips.Api.Contracts;

/**
 * Returns a temporary review draft; creating it never saves or replaces a trip.
 * @param ImportId Opaque identifier of the owner's temporary source document.
 * @param TripId Existing owned trip whose itinerary remains unchanged.
 * @param FileName Sanitized display name of the source PDF.
 * @param ExpiresAt Time after which the temporary source is unavailable.
 * @param Fields Supported proposed values and explicit uncertainty flags.
 */
public sealed record TicketImportDraftResponse(Guid ImportId, Guid TripId, string FileName,
    DateTimeOffset ExpiresAt, IReadOnlyList<TicketImportFieldResponse> Fields);
