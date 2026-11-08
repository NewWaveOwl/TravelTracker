using TravelTracker.Data.Repositories;
using TravelTracker.Trips.Data.Repositories;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Trips.Api.Tests.Support;

/**
 * Owner-aware test store for HTTP tests. Relational guarantees are proven in TravelTracker.Trips.Data.Tests
 * against PostgreSQL; this store only records what the HTTP layer asked it to write.
 */
internal sealed class InMemoryTripRepository : ITripRepository
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (Trip Trip, string Issuer, string Subject)> trips = [];

    /** Exception thrown by the next write, after which nothing is stored. */
    internal Exception? NextWriteFailure { get; set; }

    /** Number of trips currently stored. */
    internal int Count
    {
        get
        {
            lock (gate) return trips.Count;
        }
    }

    /**
     * Stores the trip unless a failure was scripted for this write.
     *
     * @param trip The validated trip.
     * @param ownerIssuer The owner's issuer.
     * @param ownerSubject The owner's subject.
     * @param cancellationToken Ignored.
     * @return A completed task, or a faulted task when a failure was scripted.
     */
    public Task AddAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (NextWriteFailure is { } failure)
            {
                NextWriteFailure = null;
                return Task.FromException(failure);
            }
            trips[trip.TripId] = (trip, ownerIssuer, ownerSubject);
            return Task.CompletedTask;
        }
    }

    /**
     * Replaces the existing trip only when its owner matches.
     *
     * @param trip The validated replacement.
     * @param ownerIssuer The caller's issuer.
     * @param ownerSubject The caller's subject.
     * @param cancellationToken Ignored by the test store.
     * @return Whether an owned trip was replaced.
     */
    public Task<bool> ReplaceOwnedAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!trips.TryGetValue(trip.TripId, out var existing) || existing.Issuer != ownerIssuer || existing.Subject != ownerSubject)
                return Task.FromResult(false);
            if (NextWriteFailure is { } failure)
            {
                NextWriteFailure = null;
                return Task.FromException<bool>(failure);
            }
            trips[trip.TripId] = (trip, ownerIssuer, ownerSubject);
            return Task.FromResult(true);
        }
    }

    /**
     * Returns the trip only when the caller is its owner.
     *
     * @param tripId The public trip identifier.
     * @param ownerIssuer The caller's issuer.
     * @param ownerSubject The caller's subject.
     * @param cancellationToken Ignored.
     * @return The trip, or null when absent or owned by someone else.
     */
    public Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult(trips.TryGetValue(tripId, out var entry) && entry.Issuer == ownerIssuer && entry.Subject == ownerSubject
                ? entry.Trip
                : null);
        }
    }

    /**
     * Returns the stored trip for a test that needs to inspect it regardless of owner.
     *
     * @param tripId The public trip identifier.
     * @return The trip, or null when none is stored.
     */
    internal Trip? Find(Guid tripId)
    {
        lock (gate)
        {
            return trips.TryGetValue(tripId, out var entry) ? entry.Trip : null;
        }
    }
}
