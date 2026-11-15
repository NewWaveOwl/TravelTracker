namespace TravelTracker.Trips.Data.Repositories;

/** describes the result of an owner- and revision-scoped replacement. */
public enum TripReplacementStatus
{
    /** The replacement committed with the next revision. */
    Replaced,

    /** No trip with that identifier is owned by the caller. */
    NotFound,

    /** The stored revision no longer matches the one the caller named. */
    Stale
}
