namespace TravelTracker.Frontend.Api.Trips;

/** A saved itinerary entry in the caller's trip history. */
public sealed record TripSummaryDto(Guid TripId, string Name, DateTimeOffset Start, DateTimeOffset End);
