namespace TravelTracker.Trips.Domain.Models;

/** The saved trip information needed to find and reopen an itinerary. */
public sealed record TripHistoryEntry(Guid TripId, string Name, DateTimeOffset Start, DateTimeOffset End);
