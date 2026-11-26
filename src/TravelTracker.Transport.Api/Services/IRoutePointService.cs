using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Defines creation and lookup of reusable shared locations.
 */
public interface IRoutePointService
{
    /**
     * Publishes an owned place or acknowledges a previously shared place.
     * @param id Existing public identifier.
     * @param viewerId Verified caller account.
     * @param cancellationToken Cancels storage work.
     * @return Whether the place is now shared.
     */
    Task<bool> ShareAsync(Guid id, Guid viewerId, CancellationToken cancellationToken);

    /**
     * Reads a point.
     * @param routePointId Identifier to find.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the point, or null when absent.
     */
    Task<RoutePoint?> GetByIdAsync(Guid routePointId, Guid viewerId, CancellationToken cancellationToken = default);

    /**
     * Creates a point from validated fields.
     * @param point Writable point values; its identifier is replaced.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the saved point.
     */
    Task<RoutePoint> CreateAsync(RoutePoint point, Guid? ownerId, CancellationToken cancellationToken = default);

    /**
     * Reads saved location candidates.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @param cancellationToken Cancels I/O.
     * @return Saved reusable locations.
     */
    Task<List<RoutePoint>> GetAllAsync(Guid viewerId, string? type = null, string? name = null, CancellationToken cancellationToken = default);
}
