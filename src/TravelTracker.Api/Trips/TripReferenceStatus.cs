namespace TravelTracker.Api.Trips;

/**
 * Outcome of checking a draft trip before saving. Only Ready allows a write.
 */
public enum TripReferenceStatus {
    /** Every reference and rule passed; the snapshot trip may be saved. */
    Ready,
    /** A referenced place or transport mode is absent or not available to the caller. */
    ReferenceRejected,
    /** The account is unknown, banned or deactivated, so it may not save trips. */
    AccountRejected,
    /** The caller's credential was rejected or the caller identity was incomplete. */
    Unauthorized,
    /** A required dependency could not confirm the data; nothing may be saved. */
    DependencyUnavailable,
    /** The itinerary broke a trip rule. */
    InvalidTrip,
    /** The trip to replace does not exist or is not owned by the caller. */
    TripNotFound,
    /** Replacement did not include the required If-Match header. */
    RevisionRequired,
    /** The requested revision does not match the stored one. */
    StaleRevision
}
