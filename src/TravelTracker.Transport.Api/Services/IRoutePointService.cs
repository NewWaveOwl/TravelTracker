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
     * @return A task containing the point, or null when absent.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId, CancellationToken cancellationToken = default);

    /**
     * Creates a point from validated fields.
     * @param point Writable point values; its identifier is replaced.
     * @return A task containing the saved point.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<RoutePoint> CreateAsync(RoutePoint point, CancellationToken cancellationToken = default);
    /** Reads saved location candidates.
     * @param cancellationToken Cancels I/O.
     * @return Saved reusable locations.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    Task<List<RoutePoint>> GetAllAsync(CancellationToken cancellationToken = default, string? type = null, string? name = null);
}
