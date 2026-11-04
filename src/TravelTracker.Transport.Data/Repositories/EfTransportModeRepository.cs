using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Entities;
using TravelTracker.Transport.Domain.Models;
using TravelTracker.Transport.Domain.Types;

namespace TravelTracker.Transport.Data.Repositories;

/** Persists detached resource values in the service-owned database.
 * @param database Scoped Transport context.
 */
public sealed class EfTransportModeRepository(TransportDbContext database) : ITransportModeRepository
{
    /** Reads saved selection candidates.
     * @param cancellationToken Cancels I/O.
     * @return Detached saved records.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    public async Task<List<TransportMode>> GetAllAsync(CancellationToken cancellationToken = default, string? code = null, string? name = null)
    {
        var query = database.TransportModes.AsNoTracking();
        if (code is not null) query = query.Where(row => row.Code == code);
        var records = await query.ToListAsync(cancellationToken);
        var search = name?.Trim();
        // Ordinal matching/order stays provider-independent; category filtering runs in SQL.
        return records.Where(row => string.IsNullOrEmpty(search) || row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.PublicId).Select(ToDomain).ToList();
    }

    /** Reads a resource by public identity.
     * @param transportModeId Public identity.
     * @param cancellationToken Cancels I/O.
     * @return Saved record or null.
     */
    public async Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default)
    {
        var row = await database.TransportModes.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == transportModeId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /** Saves a new resource atomically.
     * @param transportMode Resource with app-issued identity.
     * @param cancellationToken Cancels I/O.
     * @return Completion after durable storage.
     */
    public async Task AddAsync(TransportMode transportMode, CancellationToken cancellationToken = default)
    {
        var record = transportMode;
        database.TransportModes.Add(new TransportModeRow { Id = (long)record.TransportModeId.Key, PublicId = record.TransportModeId.PublicId, Code = record.Code, DisplayName = record.DisplayName, Description = record.Description });
        await database.SaveChangesAsync(cancellationToken);
    }

    /** Finds a saved entry by exact mode code.
     * @param code Lowercase mode code.
     * @param cancellationToken Cancels I/O.
     * @return First matching entry or null.
     */
    public async Task<TransportMode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var row = await database.TransportModes.AsNoTracking().Where(row => row.Code == code).OrderBy(row => row.PublicId).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /** Replaces saved catalogue values.
     * @param record Existing identity with replacement values.
     * @param cancellationToken Cancels I/O.
     * @return Completion after saving.
     */
    public async Task UpdateAsync(TransportMode record, CancellationToken cancellationToken = default)
    {
        var row = await database.TransportModes.SingleOrDefaultAsync(row => row.PublicId == record.TransportModeId.PublicId, cancellationToken);
        if (row is null) return;
        row.Code = record.Code; row.DisplayName = record.DisplayName; row.Description = record.Description;
        await database.SaveChangesAsync(cancellationToken);
    }

    /** Restores a detached domain identity and values.
     * @param row Persisted resource.
     * @return Domain resource with the same public identity.
     */
    private static TransportMode ToDomain(TransportModeRow row) => new() { TransportModeId = new(new Id64(row.Id), row.PublicId), Code = row.Code, DisplayName = row.DisplayName, Description = row.Description };
}
