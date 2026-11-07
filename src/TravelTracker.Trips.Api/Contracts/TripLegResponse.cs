namespace TravelTracker.Trips.Api.Contracts;

/**
 * One ordered leg of a saved trip, with its snapshots and absolute times.
 *
 * @param Sequence One-based position in the trip.
 * @param From Departure place snapshot.
 * @param To Arrival place snapshot.
 * @param Transport Transport mode snapshot.
 * @param Departure Departure time with its offset.
 * @param Arrival Arrival time with its offset.
 */
public sealed record TripLegResponse(int Sequence, TripPlaceResponse From, TripPlaceResponse To, TripTransportResponse Transport,
    DateTimeOffset Departure, DateTimeOffset Arrival);
