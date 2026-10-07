using System.Collections.Concurrent;
using TravelTracker.Data.SeedData;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Keeps immutable shared route points for this API process.
 */
public sealed class InMemoryRoutePointRepository : IRoutePointRepository
{
    private readonly ConcurrentDictionary<Guid, RoutePoint> records =
        new(TransportSeedData.Points().ToDictionary(point => point.RoutePointId));

    /**
     * Finds a saved location.
     * @param routePointId Identifier to find.
     * @return A task containing the point, or null when absent.
     */
    public Task<RoutePoint?> GetByIdAsync(Guid routePointId) => Task.FromResult(records.GetValueOrDefault(routePointId));

    /**
     * Stores a newly created location.
     * @param point Point with a generated identifier.
     * @return Completion after storage.
     */
    public Task AddAsync(RoutePoint point)
    {
        records[point.RoutePointId] = point;
        return Task.CompletedTask;
    }
}
