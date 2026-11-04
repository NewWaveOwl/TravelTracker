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
     * @param cancellationToken Cancels database I/O.
     * @return Detached rows selected for the caller.
     */
    public async Task<List<Attraction>> GetVisibleAsync(string issuer, string subject, bool includeShared, CancellationToken cancellationToken = default) =>
        (await database.Attractions.AsNoTracking().Where(row => row.IsShared && includeShared ||
            !row.IsShared && row.OwnerIssuer == issuer && row.OwnerSubject == subject)
            .ToListAsync(cancellationToken)).Select(ToDomain).ToList();
    /**
     * Locks a row until the write session commits or disposes.
     * @param attractionId The target public UUID.
     * @return An explicit transactional write session.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<IAttractionWriteSession> BeginWriteAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await database.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Attractions\" WHERE \"PublicId\" = {attractionId} FOR UPDATE", cancellationToken);
            return new AttractionWriteSession(transaction);
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    /**

     * @return Independent domain copies of all stored attractions.

     */
    public async Task<List<Attraction>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await database.Attractions.AsNoTracking().ToListAsync(cancellationToken)).Select(ToDomain).ToList();

    /**
     * Loads a public identifier without exposing a tracked row.
     * @param attractionId The public identifier.
     * @return The domain copy, or null if absent.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        var row = await database.Attractions.AsNoTracking().SingleOrDefaultAsync(row => row.PublicId == attractionId, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Persists a new row with an application-issued identity.
     * @param attraction The validated domain record.
     * @return Completion after saving.
     * @param cancellationToken Cancels database work for the request.
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
     * @return Completion after staging the database update.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task UpdateAsync(Attraction attraction, CancellationToken cancellationToken = default)
    {
        database.ChangeTracker.Clear();
        database.Attractions.Update(ToRow(attraction));
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
    }

    /**
     * Deletes an existing row through the internal repository contract.
     * @param attractionId The public identifier.
     * @return Whether a row was deleted.
     * @param cancellationToken Cancels database work for the request.
     */
    public async Task<bool> DeleteAsync(Guid attractionId, CancellationToken cancellationToken = default) =>
        await database.Attractions.Where(row => row.PublicId == attractionId).ExecuteDeleteAsync(cancellationToken) != 0;

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
