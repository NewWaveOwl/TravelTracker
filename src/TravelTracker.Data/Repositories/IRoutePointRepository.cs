using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Defines storage for the new shared route-point model.
 */
public interface IRoutePointRepository
{
    /**
     * Finds a location.
     * @param routePointId Identifier to find.
     * @return A task containing the immutable point, or null when absent.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId);

    /**
     * Stores a new location.
     * @param point Point created by the service.
     * @return Completion after storage.
     */
    Task AddAsync(RoutePoint point);
}
