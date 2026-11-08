namespace TravelTracker.Frontend.Api.Trips;

/** One saved leg of a trip. */
public sealed record TripLegDto(int Sequence, TripPlaceDto From, TripPlaceDto To, TripTransportDto Transport, DateTimeOffset Departure, DateTimeOffset Arrival);
