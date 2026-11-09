namespace TravelTracker.Trips.Api.Contracts;

/** A saved trip entry in the caller's history. */
public sealed record TripSummaryResponse(Guid TripId, string Name, DateTimeOffset Start, DateTimeOffset End);
