using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Defines storage for the new shared route-point model.
 */
public interface IRoutePointRepository
{
    /**
     * Finds a location.
     * @param routePointId Identifier to find.
     * @return A task containing the immutable point, or null when absent.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId, CancellationToken cancellationToken = default);

    /**
     * Stores a new location.
     * @param point Point created by the service.
     * @return Completion after storage.
      * @param cancellationToken Cancels storage I/O.
     */
    Task AddAsync(RoutePoint point, CancellationToken cancellationToken = default);
    /** Reads saved location candidates.
     * @param cancellationToken Cancels I/O.
     * @return Saved reusable locations.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    Task<List<RoutePoint>> GetAllAsync(CancellationToken cancellationToken = default, string? type = null, string? name = null);
}
