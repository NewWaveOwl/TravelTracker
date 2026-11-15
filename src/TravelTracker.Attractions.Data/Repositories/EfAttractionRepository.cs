using Microsoft.EntityFrameworkCore;
using TravelTracker.Attractions.Data.Entities;
using TravelTracker.Attractions.Domain.Models;
using TravelTracker.Attractions.Domain.Types;

namespace TravelTracker.Attractions.Data.Repositories;

/**
 * Loads and stores attractions exclusively in the service-owned PostgreSQL database.
 * @param database The request-scoped context.
 */
public sealed class EfAttractionRepository(AttractionsDbContext database) : IAttractionRepository
{
    /**
     * Filters the application's visibility selection before materializing records.
     * @param issuer The selected owner issuer.
     * @param subject The selected owner subject.
     * @param includeShared Whether to include shared catalogue rows.
     * @param nameFilter Optional case-insensitive name substring.
     * @param cancellationToken Cancels database I/O.
     * @return Detached rows selected for the caller, ordered by name then public identifier.
     */
    public async Task<List<Attraction>> GetVisibleAsync(string issuer, string subject, bool includeShared, string? nameFilter = null, CancellationToken cancellationToken = default)
    {
        var visible = database.Attractions.AsNoTracking().Where(row => row.IsShared && includeShared ||
            !row.IsShared && row.OwnerIssuer == issuer && row.OwnerSubject == subject);
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var needle = nameFilter.Trim().ToLowerInvariant();
            visible = visible.Where(row => row.Name.ToLower().Contains(needle));
        }
        var rows = await visible.OrderBy(row => row.Name.ToLower()).ThenBy(row => row.PublicId).ToListAsync(cancellationToken);
        return rows.Select(ToDomain).ToList();
    }

    /**
     * Locks a row until the write session commits or disposes.
     * @param attractionId The target public UUID.
     * @param cancellationToken Cancels database work for the request.
     * @return An explicit transactional write session.
     */
    public async Task<IAttractionWriteSession> BeginWriteAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Attractions\" WHERE \"PublicId\" = {attractionId} FOR UPDATE", cancellationToken);
            return new AttractionWriteSession(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    /**
     * Loads a public identifier without exposing a tracked row.
     * @param attractionId The public identifier.
     * @param cancellationToken Cancels database work for the request.
     * @return The domain copy, or null if absent.
     */
    public async Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var row = await database.Attractions.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.PublicId == attractionId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Persists a new row with an application-issued identity.
     * @param attraction The validated domain record.
     * @param cancellationToken Cancels database work for the request.
     * @return Completion after saving.
     */
    public async Task AddAsync(Attraction attraction, CancellationToken cancellationToken = default)
    {
        database.Attractions.Add(ToRow(attraction));
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
    }

    /**
     * Saves an accepted candidate inside its caller's row-locking transaction.
     * @param attraction The candidate with its preserved identity.
     * @param cancellationToken Cancels database work for the request.
     * @return Completion after staging the database update.
     */
    public async Task UpdateAsync(Attraction attraction, CancellationToken cancellationToken = default)
    {
        database.ChangeTracker.Clear();
        database.Attractions.Update(ToRow(attraction));
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
    }

    /**
     * Converts persisted values into an independent domain entity.
     * @param row The persistence row.
     * @return Its domain representation.
     */
    private static Attraction ToDomain(AttractionRow row) => new()
    {
        AttractionId = new EntityId<Attraction>(new Id64(row.Id), row.PublicId), Name = row.Name,
        Address = row.Address, Latitude = row.Latitude, Longitude = row.Longitude,
        Description = row.Description, Rating = row.Rating, EstimatedVisitMinutes = row.EstimatedVisitMinutes,
        OwnerIssuer = row.OwnerIssuer, OwnerSubject = row.OwnerSubject, IsShared = row.IsShared
    };

    /**
     * Converts server-owned identity and accepted values to storage.
     * @param record The accepted domain record.
     * @return A detached database row.
     */
    private static AttractionRow ToRow(Attraction record) => new()
    {
        Id = (long)record.AttractionId.Key, PublicId = record.AttractionId.PublicId, Name = record.Name,
        Address = record.Address, Latitude = record.Latitude, Longitude = record.Longitude,
        Description = record.Description, Rating = record.Rating, EstimatedVisitMinutes = record.EstimatedVisitMinutes,
        OwnerIssuer = record.OwnerIssuer, OwnerSubject = record.OwnerSubject, IsShared = record.IsShared
    };
}
