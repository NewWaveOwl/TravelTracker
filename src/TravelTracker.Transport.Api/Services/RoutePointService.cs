using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Creates and reads reusable locations through the Transport Data layer.
 * @param repository Scoped durable location storage.
 * @param ids Issues new entity identities.
 */
public sealed class RoutePointService(IRoutePointRepository repository, EntityIdGenerator ids) : IRoutePointService
{
    /**
     * Reads one location.
     * @param routePointId Identifier to find.
     * @return A task containing the point, or null when absent.
      * @param cancellationToken Cancels storage I/O.
     */
    public Task<RoutePoint?> GetByIdAsync(Guid routePointId, CancellationToken cancellationToken = default) => repository.GetByIdAsync(routePointId, cancellationToken);

    /**
     * Generates identity and trims the name before saving validated values.
     * @param point Writable point values.
     * @return A task containing the saved point.
      * @param cancellationToken Cancels storage I/O.
     */
    public async Task<RoutePoint> CreateAsync(RoutePoint point, CancellationToken cancellationToken = default)
    {
        var saved = point with { RoutePointId = await ids.NextAsync<RoutePoint>(cancellationToken), Name = point.Name.Trim() };
        await repository.AddAsync(saved, cancellationToken);
        return saved;
    }
    /** Reads saved locations for selection.
     * @param cancellationToken Cancels I/O.
     * @return Saved reusable locations.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    public Task<List<RoutePoint>> GetAllAsync(CancellationToken cancellationToken = default, string? type = null, string? name = null) => repository.GetAllAsync(cancellationToken, type, name);
}
