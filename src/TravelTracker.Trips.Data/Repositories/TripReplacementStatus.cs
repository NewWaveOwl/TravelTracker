namespace TravelTracker.Trips.Data.Repositories;

/** Describes the result of an owner- and revision-scoped replacement. */
public enum TripReplacementStatus
{
    Replaced,
    NotFound,
    Stale
}
