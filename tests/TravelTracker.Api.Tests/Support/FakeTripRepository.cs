using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Tests.Support;

/**
 * In-memory stand-in for trip storage, so save tests can prove whether a write happened.
 * Owner filtering and identifiers belong to the persistence feature (#41); this fake returns every stored trip.
 */
public sealed class FakeTripRepository : ITripRepository {
    private readonly List<Trip> stored = [];

    /** Trips in the order they were added. */
    public IReadOnlyList<Trip> Stored => stored;

    /**
     * Returns every stored trip, because this fake does not track owners.
     *
     * @param userId Ignored by this fake.
     * @return A copy of the stored trips.
     */
    public Task<List<Trip>> GetAllByUserIdAsync(Guid userId) => Task.FromResult(stored.ToList());

    /**
     * Not used by the #40 tests.
     *
     * @param tripId Ignored.
     * @return Never returns.
     * @throws NotSupportedException Always.
     */
    public Task<Trip?> GetByIdAsync(Guid tripId) => throw new NotSupportedException("trip identifiers belong to #41.");

    /**
     * Records a stored trip.
     *
     * @param trip The trip to store.
     * @return A completed task.
     */
    public Task AddAsync(Trip trip) {
        stored.Add(trip);
        return Task.CompletedTask;
    }

    /**
     * Not used by the #40 tests.
     *
     * @param trip Ignored.
     * @return Never returns.
     * @throws NotSupportedException Always.
     */
    public Task UpdateAsync(Trip trip) => throw new NotSupportedException("trip updates belong to #41.");

    /**
     * Not used by the #40 tests.
     *
     * @param tripId Ignored.
     * @return Never returns.
     * @throws NotSupportedException Always.
     */
    public Task<bool> DeleteAsync(Guid tripId) => throw new NotSupportedException("trip deletion belongs to #41.");

    /**
     * Not used by the #40 tests.
     *
     * @param tripId Ignored.
     * @param legs Ignored.
     * @return Never returns.
     * @throws NotSupportedException Always.
     */
    public Task ReplaceLegsAsync(Guid tripId, List<TripLeg> legs) => throw new NotSupportedException("leg replacement belongs to #41.");
}
