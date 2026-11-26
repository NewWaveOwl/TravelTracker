namespace TravelTracker.Trips.Api.Contracts;

/**
 * Returns a pasted-text review draft; nothing is stored, so there is no temporary source and the trip is unchanged.
 * @param TripId Existing owned trip whose itinerary remains unchanged.
 * @param Fields Proposed values in the same named shape as the PDF import, with explicit uncertainty flags.
 */
public sealed record TicketTextDraftResponse(Guid TripId, IReadOnlyList<TicketImportFieldResponse> Fields);
