using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Defines creation and lookup of reusable shared locations.
 */
public interface IRoutePointService
{
    /**
     * Reads a point.
     * @param routePointId Identifier to find.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the point, or null when absent.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId, CancellationToken cancellationToken = default);

    /**
     * Creates a point from validated fields.
     * @param point Writable point values; its identifier is replaced.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the saved point.
     */
    Task<RoutePoint> CreateAsync(RoutePoint point, CancellationToken cancellationToken = default);

    /**
     * Reads saved location candidates.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @param cancellationToken Cancels I/O.
     * @return Saved reusable locations.
     */
    Task<List<RoutePoint>> GetAllAsync(string? type = null, string? name = null, CancellationToken cancellationToken = default);
}
