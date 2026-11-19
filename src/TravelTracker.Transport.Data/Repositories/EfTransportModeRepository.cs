using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Entities;
using TravelTracker.Transport.Domain.Models;
using TravelTracker.Transport.Domain.Types;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Persists detached resource values in the service-owned database.
 * @param database Scoped Transport context.
 */
public sealed class EfTransportModeRepository(TransportDbContext database) : ITransportModeRepository
{
    /**
     * Reads saved selection candidates.
     * @param cancellationToken Cancels I/O.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @return Detached saved records.
     */
    public async Task<List<TransportMode>> GetAllAsync(CancellationToken cancellationToken = default, string? code = null, string? name = null)
    {
        var query = database.TransportModes.AsNoTracking();
        if (code is not null) query = query.Where(row => row.Code == code);
        var records = await query.ToListAsync(cancellationToken);
        var search = name?.Trim();
        // ordinal matching/order stays provider-independent; category filtering runs in SQL.
        // ponytail: name filter runs in memory over the whole table, move to SQL and add paging when the catalogue grows
        return records.Where(candidate => string.IsNullOrEmpty(search) || candidate.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(candidate => candidate.PublicId).Select(ToDomain).ToList();
    }

    /**
     * Reads a resource by public identity.
     * @param transportModeId Public identity.
     * @param cancellationToken Cancels I/O.
     * @return Saved record or null.
     */
    public async Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default)
    {
        var row = await database.TransportModes.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PublicId == transportModeId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Saves a new resource atomically.
     * @param transportMode Resource with app-issued identity.
     * @param cancellationToken Cancels I/O.
     * @return Completion after durable storage.
     */
    public async Task AddAsync(TransportMode transportMode, CancellationToken cancellationToken = default)
    {
        database.TransportModes.Add(new TransportModeRow
        {
            Id = (long)transportMode.TransportModeId.Key,
            PublicId = transportMode.TransportModeId.PublicId,
            Code = transportMode.Code,
            DisplayName = transportMode.DisplayName,
            Description = transportMode.Description
        });
        await database.SaveChangesAsync(cancellationToken);
    }

    /**
     * Restores a detached domain identity and values.
     * @param row Persisted resource.
     * @return Domain resource with the same public identity.
     */
    private static TransportMode ToDomain(TransportModeRow row) => new()
    {
        TransportModeId = new EntityId<TransportMode>(new Id64(row.Id), row.PublicId),
        Code = row.Code,
        DisplayName = row.DisplayName,
        Description = row.Description
    };
}
