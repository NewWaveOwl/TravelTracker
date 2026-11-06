namespace TravelTracker.Api.Trips;

/**
 * One requested leg before its references are checked.
 *
 * @param Sequence One-based position in the trip.
 * @param From Requested departure place.
 * @param To Requested arrival place.
 * @param TransportModeId Public identifier of the transport mode in the Transport service.
 * @param Departure When the journey leaves From, with its offset.
 * @param Arrival When the journey reaches To, with its offset.
 */
public sealed record TripLegDraft(int Sequence, TripPlaceReference From, TripPlaceReference To, Guid TransportModeId,
    DateTimeOffset Departure, DateTimeOffset Arrival);
