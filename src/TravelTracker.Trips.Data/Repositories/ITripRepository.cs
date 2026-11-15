using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Trips.Data.Repositories;

/**
 * defines owner-scoped trip persistence. Every write names the validated owner, and every read
 * is filtered by that owner, so another user's trip is indistinguishable from an absent one.
 * a replacement changes the trip and every leg together and preserves its owner and public identifier.
 */
public interface ITripRepository
{
    /**
     * stores a complete trip with its legs and snapshots as one unit. Nothing is kept when the write fails.
     *
     * @param trip the validated trip to store.
     * @param ownerIssuer the validated token issuer of the owner.
     * @param ownerSubject the validated token subject of the owner.
     * @param cancellationToken cancels the write.
     * @return a task that completes when the trip is committed.
     */
    Task AddAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);

    /**
     * replaces a trip and all of its legs in one write, scoped to the owner.
     *
     * @param trip the validated replacement with the existing trip's identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param expectedRevision revision named by the client.
     * @param cancellationToken cancels database I/O.
     * @return whether the replacement committed, the owned trip was absent, or its revision had changed.
     */
    Task<TripReplacementStatus> ReplaceOwnedAsync(Trip trip, string ownerIssuer, string ownerSubject, long expectedRevision,
        CancellationToken cancellationToken = default);

    /**
     * reads the revision of a trip without loading its legs.
     *
     * @param tripId public trip identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param cancellationToken cancels the read.
     * @return the current revision, or null when the trip is absent or owned by someone else.
     */
    Task<long?> GetOwnedRevisionAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);

    /**
     * retrieves a trip only when it belongs to the given owner.
     *
     * @param tripId the public identifier of the trip.
     * @param ownerIssuer the validated token issuer of the caller.
     * @param ownerSubject the validated token subject of the caller.
     * @param cancellationToken cancels the read.
     * @return a task containing the trip, or null when it is absent or owned by someone else.
     */
    Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);

    /**
     * Lists saved trips for one owner in descending trip start order.
     * @param ownerIssuer The caller's validated issuer.
     * @param ownerSubject The caller's validated subject.
     * @param cancellationToken Cancels the read.
     * @return The caller's saved trip summaries.
     */
    Task<IReadOnlyList<TripHistoryEntry>> ListOwnedAsync(string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default);
}
