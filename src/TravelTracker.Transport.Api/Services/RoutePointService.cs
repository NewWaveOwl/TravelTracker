using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Creates and reads reusable locations through the Transport Data layer.
 * @param repository Scoped durable location storage.
 * @param identityGenerator Issues new entity identities.
 */
public sealed class RoutePointService(IRoutePointRepository repository, EntityIdGenerator identityGenerator) : IRoutePointService
{
    /**
     * Publishes an owned place or acknowledges a previously shared place.
     * @param id Existing public identifier.
     * @param viewerId Verified caller account.
     * @param cancellationToken Cancels storage work.
     * @return Whether the place is now shared.
     */
    public Task<bool> ShareAsync(Guid id, Guid viewerId, CancellationToken cancellationToken) =>
        repository.ShareAsync(id, viewerId, cancellationToken);

    /**
     * Reads one location.
     * @param routePointId Identifier to find.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the point, or null when absent.
     */
    public Task<RoutePoint?> GetByIdAsync(Guid routePointId, Guid viewerId, CancellationToken cancellationToken = default) => repository.GetByIdAsync(routePointId, viewerId, cancellationToken);

    /**
     * Generates identity and trims the name before saving validated values.
     * @param point Writable point values.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the saved point.
     */
    public async Task<RoutePoint> CreateAsync(RoutePoint point, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        var saved = point with { RoutePointId = await identityGenerator.NextAsync<RoutePoint>(cancellationToken), Name = point.Name.Trim() };
        await repository.AddAsync(saved, ownerId, cancellationToken);
        return saved;
    }

    /**
     * Reads saved locations for selection.
     * @param cancellationToken Cancels I/O.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @return Saved reusable locations.
     */
    public Task<List<RoutePoint>> GetAllAsync(Guid viewerId, string? type = null, string? name = null, CancellationToken cancellationToken = default) => repository.GetAllAsync(viewerId, cancellationToken, type, name);
}
