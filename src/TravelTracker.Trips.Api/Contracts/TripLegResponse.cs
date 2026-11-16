namespace TravelTracker.Trips.Api.Contracts;

/**
 * one ordered leg of a saved trip, with its snapshots and absolute times.
 *
 * @param Sequence one-based position in the trip.
 * @param From departure place snapshot.
 * @param To arrival place snapshot.
 * @param Transport Transport mode snapshot.
 * @param Departure departure time with its offset.
 * @param Arrival arrival time with its offset.
 */
public sealed record TripLegResponse(int Sequence, TripPlaceResponse From, TripPlaceResponse To, TripTransportResponse Transport,
    DateTimeOffset Departure, DateTimeOffset Arrival);
