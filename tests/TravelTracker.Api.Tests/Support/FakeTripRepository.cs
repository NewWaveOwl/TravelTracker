using TravelTracker.Data.Repositories;
using TravelTracker.Trips.Data.Repositories;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Tests.Support;

/**
 * In-memory stand-in for trip storage, so save tests can prove whether a write happened and who owns it.
 * It enforces owner filtering like the PostgreSQL store, and can be told to fail the next write.
 */
public sealed class FakeTripRepository : ITripRepository {
    private readonly List<Trip> stored = [];
    private readonly Dictionary<Guid, (string Issuer, string Subject)> owners = [];

    /** Trips in the order they were added. */
    public IReadOnlyList<Trip> Stored => stored;

    /** Exception thrown by the next AddAsync call, and nothing is stored when it is set. */
    public Exception? NextWriteFailure { get; set; }

    /**
     * Records a stored trip with its owner.
     *
     * @param trip The trip to store.
     * @param ownerIssuer The owner's issuer.
     * @param ownerSubject The owner's subject.
     * @param cancellationToken Ignored by this fake.
     * @return A completed task, or a faulted task when a failure was set.
     */
    public Task AddAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default) {
        if (NextWriteFailure is { } failure) {
            NextWriteFailure = null;
            return Task.FromException(failure);
        }
        stored.Add(trip);
        owners[trip.TripId] = (ownerIssuer, ownerSubject);
        return Task.CompletedTask;
    }

    /**
     * Returns a stored trip only when the caller owns it.
     *
     * @param tripId The public trip identifier.
     * @param ownerIssuer The caller's issuer.
     * @param ownerSubject The caller's subject.
     * @param cancellationToken Ignored by this fake.
     * @return The trip, or null when absent or owned by someone else.
     */
    public Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default) {
        var trip = stored.FirstOrDefault(candidate => candidate.TripId == tripId);
        var owned = trip is not null && owners.TryGetValue(tripId, out var owner) && owner == (ownerIssuer, ownerSubject);
        return Task.FromResult(owned ? trip : null);
    }
}
