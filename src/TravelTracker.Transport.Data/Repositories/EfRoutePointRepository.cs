using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Entities;
using TravelTracker.Transport.Domain.Models;
using TravelTracker.Transport.Domain.Types;

namespace TravelTracker.Transport.Data.Repositories;

/** Persists detached resource values in the service-owned database.
 * @param database Scoped Transport context.
 */
public sealed class EfRoutePointRepository(TransportDbContext database) : IRoutePointRepository
{
    /** Reads saved selection candidates.
     * @param cancellationToken Cancels I/O.
     * @return Detached saved records.
     * @param type Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    public async Task<List<RoutePoint>> GetAllAsync(CancellationToken cancellationToken = default, string? type = null, string? name = null)
    {
        var query = database.RoutePoints.AsNoTracking();
        if (type is not null) query = query.Where(row => row.Type == type);
        var records = await query.ToListAsync(cancellationToken);
        var search = name?.Trim();
        // Ordinal matching/order stays provider-independent; category filtering runs in SQL.
        return records.Where(row => string.IsNullOrEmpty(search) || row.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.PublicId).Select(ToDomain).ToList();
    }

    /** Reads a resource by public identity.
     * @param routePointId Public identity.
     * @param cancellationToken Cancels I/O.
     * @return Saved record or null.
     */
    public async Task<RoutePoint?> GetByIdAsync(Guid routePointId, CancellationToken cancellationToken = default)
    {
        var row = await database.RoutePoints.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == routePointId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /** Saves a new resource atomically.
     * @param point Resource with app-issued identity.
     * @param cancellationToken Cancels I/O.
     * @return Completion after durable storage.
     */
    public async Task AddAsync(RoutePoint point, CancellationToken cancellationToken = default)
    {
        var record = point;
        database.RoutePoints.Add(new RoutePointRow { Id = (long)record.RoutePointId.Key, PublicId = record.RoutePointId.PublicId, Type = record.Type, Name = record.Name, Address = record.Address, Latitude = record.Latitude, Longitude = record.Longitude, SourceId = record.SourceId });
        await database.SaveChangesAsync(cancellationToken);
    }

    /** Restores a detached domain identity and values.
     * @param row Persisted resource.
     * @return Domain resource with the same public identity.
     */
    private static RoutePoint ToDomain(RoutePointRow row) => new(new(new Id64(row.Id), row.PublicId), row.Type, row.Name, row.Address, row.Latitude, row.Longitude, row.SourceId);
}
