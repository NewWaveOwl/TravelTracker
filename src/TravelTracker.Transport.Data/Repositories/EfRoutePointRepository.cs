using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Entities;
using TravelTracker.Transport.Domain.Models;
using TravelTracker.Transport.Domain.Types;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Persists detached resource values in the service-owned database.
 * @param database Scoped Transport context.
 */
public sealed class EfRoutePointRepository(TransportDbContext database) : IRoutePointRepository
{
    public async Task<bool> ShareAsync(Guid id, Guid viewerId, CancellationToken cancellationToken)
    {
        var row = await database.RoutePoints.SingleOrDefaultAsync(row => row.PublicId == id && (row.IsShared || row.OwnerId == viewerId), cancellationToken);
        if (row is null) return false;
        if (row.IsShared) return true;
        row.IsShared = true;
        await database.SaveChangesAsync(cancellationToken);
        return true;
    }

    /**
     * Reads saved selection candidates.
     * @param cancellationToken Cancels I/O.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @return Detached saved records.
     */
    public async Task<List<RoutePoint>> GetAllAsync(Guid viewerId, CancellationToken cancellationToken = default, string? type = null, string? name = null)
    {
        var query = VisibleTo(viewerId);
        if (type is not null) query = query.Where(row => row.Type == type);
        var records = await query.ToListAsync(cancellationToken);
        var search = name?.Trim();
        // ordinal matching/order stays provider-independent; category filtering runs in SQL.
        // ponytail: name filter runs in memory over the whole table, move to SQL and add paging when the catalogue grows
        return records.Where(candidate => string.IsNullOrEmpty(search) || candidate.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase).ThenBy(candidate => candidate.PublicId).Select(ToDomain).ToList();
    }

    /**
     * Reads a resource by public identity.
     * @param routePointId Public identity.
     * @param cancellationToken Cancels I/O.
     * @return Saved record or null.
     */
    public async Task<RoutePoint?> GetByIdAsync(Guid routePointId, Guid viewerId, CancellationToken cancellationToken = default)
    {
        var row = await VisibleTo(viewerId).SingleOrDefaultAsync(candidate => candidate.PublicId == routePointId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Saves a new resource atomically.
     * @param point Resource with app-issued identity.
     * @param cancellationToken Cancels I/O.
     * @return Completion after durable storage.
     */
    public async Task AddAsync(RoutePoint point, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        database.RoutePoints.Add(new RoutePointRow
        {
            Id = (long)point.RoutePointId.Key,
            OwnerId = ownerId,
            IsShared = ownerId is null,
            PublicId = point.RoutePointId.PublicId,
            Type = point.Type,
            Name = point.Name,
            Address = point.Address,
            Latitude = point.Latitude,
            Longitude = point.Longitude,
            SourceId = point.SourceId
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    IQueryable<RoutePointRow> VisibleTo(Guid viewerId) => database.RoutePoints.AsNoTracking()
        .Where(point => point.IsShared || point.OwnerId == viewerId);

    /**
     * Restores a detached domain identity and values.
     * @param row Persisted resource.
     * @return Domain resource with the same public identity.
     */
    private static RoutePoint ToDomain(RoutePointRow row) =>
        new(new EntityId<RoutePoint>(new Id64(row.Id), row.PublicId), row.Type, row.Name, row.Address, row.Latitude, row.Longitude, row.SourceId);
}
