using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Creates and reads reusable locations through the shared Data layer.
 * @param repository Process-lifetime location storage.
 */
public sealed class RoutePointService(IRoutePointRepository repository) : IRoutePointService
{
    /**
     * Reads one location.
     * @param routePointId Identifier to find.
     * @return A task containing the point, or null when absent.
     */
    public Task<RoutePoint?> GetByIdAsync(Guid routePointId) => repository.GetByIdAsync(routePointId);

    /**
     * Generates identity and trims the name before saving validated values.
     * @param point Writable point values.
     * @return A task containing the saved point.
     */
    public async Task<RoutePoint> CreateAsync(RoutePoint point)
    {
        var saved = point with { RoutePointId = Guid.NewGuid(), Name = point.Name.Trim() };
        await repository.AddAsync(saved);
        return saved;
    }
}
