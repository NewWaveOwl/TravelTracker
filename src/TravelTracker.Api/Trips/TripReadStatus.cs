namespace TravelTracker.Api.Trips;

/**
 * Outcome of reading one of the caller's own trips.
 */
public enum TripReadStatus {
    /** The trip exists and belongs to the caller, and the caller's account may read it. */
    Found,
    /** No trip with that identifier belongs to the caller. Absent and another user's trips look the same. */
    NotFound,
    /** The account is unknown or deactivated, so it may not read trips. */
    AccountRejected,
    /** The caller's identity was incomplete. */
    Unauthorized,
    /** Account standing could not be confirmed; nothing may be read. */
    DependencyUnavailable
}
