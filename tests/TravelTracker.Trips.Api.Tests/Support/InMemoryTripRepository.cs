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
     * @param expectedRevision The revision named by the client.
     * @param cancellationToken Ignored by the test store.
     * @return Whether it was stored, absent, or stale.
     */
    public Task<TripReplacementStatus> ReplaceOwnedAsync(Trip trip, string ownerIssuer, string ownerSubject, long expectedRevision, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (!trips.TryGetValue(trip.TripId, out var existing) || existing.Issuer != ownerIssuer || existing.Subject != ownerSubject)
                return Task.FromResult(TripReplacementStatus.NotFound);
            if (existing.Trip.Revision != expectedRevision)
                return Task.FromResult(TripReplacementStatus.Stale);
            if (NextWriteFailure is { } failure)
            {
                NextWriteFailure = null;
                return Task.FromException<TripReplacementStatus>(failure);
            }
            trips[trip.TripId] = (trip, ownerIssuer, ownerSubject);
            return Task.FromResult(TripReplacementStatus.Replaced);
        }
    }

    /** {@inheritDoc} */
    public async Task<long?> GetOwnedRevisionAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default) =>
        (await GetOwnedByIdAsync(tripId, ownerIssuer, ownerSubject, cancellationToken))?.Revision;

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

    /** {@inheritDoc} */
    public Task<IReadOnlyList<TripHistoryEntry>> ListOwnedAsync(string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<TripHistoryEntry>>(trips.Values
                .Where(entry => entry.Issuer == ownerIssuer && entry.Subject == ownerSubject)
                .Select(entry => entry.Trip)
                .OrderByDescending(trip => trip.Start).ThenBy(trip => trip.TripId)
                .Select(trip => new TripHistoryEntry(trip.TripId, trip.Name, trip.Start, trip.End)).ToArray());
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
