using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Trips.Data.Repositories;

/**
 * Defines owner-scoped trip persistence. Every write names the validated owner, and every read
 * is filtered by that owner, so another user's trip is indistinguishable from an absent one.
 * A replacement changes the trip and every leg together and preserves its owner and public identifier.
 */
public interface ITripRepository
{
    /**
     * Stores a complete trip with its legs and snapshots as one unit. Nothing is kept when the write fails.
     *
     * @param trip The validated trip to store.
     * @param ownerIssuer The validated token issuer of the owner.
     * @param ownerSubject The validated token subject of the owner.
     * @param cancellationToken Cancels the write.
     * @return A task that completes when the trip is committed.
     */
    Task AddAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);

    /**
     * Replaces a trip and all of its legs in one write, scoped to the owner.
     *
     * @param trip The validated replacement with the existing trip's identifier.
     * @param ownerIssuer The caller's validated issuer.
     * @param ownerSubject The caller's validated subject.
     * @param cancellationToken Cancels database I/O.
     * @param expectedRevision Revision named by the client.
     * @return Whether the replacement committed, the owned trip was absent, or its revision had changed.
     */
    Task<TripReplacementStatus> ReplaceOwnedAsync(Trip trip, string ownerIssuer, string ownerSubject, long expectedRevision,
        CancellationToken cancellationToken = default);

    /**
     * Reads the revision of a trip without loading its legs.
     *
     * @param tripId Public trip identifier.
     * @param ownerIssuer The caller's validated issuer.
     * @param ownerSubject The caller's validated subject.
     * @param cancellationToken Cancels the read.
     * @return The current revision, or null when the trip is absent or owned by someone else.
     */
    Task<long?> GetOwnedRevisionAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);

    /**
     * Retrieves a trip only when it belongs to the given owner.
     *
     * @param tripId The public identifier of the trip.
     * @param ownerIssuer The validated token issuer of the caller.
     * @param ownerSubject The validated token subject of the caller.
     * @param cancellationToken Cancels the read.
     * @return A task containing the trip, or null when it is absent or owned by someone else.
     */
    Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);
}
