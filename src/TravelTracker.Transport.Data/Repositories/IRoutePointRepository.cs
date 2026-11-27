using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Defines storage for reusable route points.
 */
public interface IRoutePointRepository
{
    Task<bool> ShareAsync(Guid id, Guid viewerId, CancellationToken cancellationToken);

    /**
     * Finds a location.
     * @param routePointId Identifier to find.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the immutable point, or null when absent.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId, Guid viewerId, CancellationToken cancellationToken = default);

    /**
     * Stores a new location.
     * @param point Point created by the service.
     * @param cancellationToken Cancels storage I/O.
     * @return Completion after storage.
     */
    Task AddAsync(RoutePoint point, Guid? ownerId, CancellationToken cancellationToken = default);

    /**
     * Reads saved location candidates.
     * @param cancellationToken Cancels I/O.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @return Saved reusable locations.
     */
    Task<List<RoutePoint>> GetAllAsync(Guid viewerId, CancellationToken cancellationToken = default, string? type = null, string? name = null);
}
