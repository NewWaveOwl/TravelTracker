using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Defines creation and lookup of reusable shared locations.
 */
public interface IRoutePointService
{
    /**
     * Reads a point.
     * @param routePointId Identifier to find.
     * @return A task containing the point, or null when absent.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId);

    /**
     * Creates a point from validated fields.
     * @param point Writable point values; its identifier is replaced.
     * @return A task containing the saved point.
     */
    Task<RoutePoint> CreateAsync(RoutePoint point);
}
