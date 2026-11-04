using Microsoft.EntityFrameworkCore;
using TravelTracker.Attractions.Data.Repositories;
using TravelTracker.Attractions.Data.SeedData;

namespace TravelTracker.Attractions.Data;

/**

 * Hosts explicit database migration and optional first-run demo initialization.

 */
public static class AttractionsDatabase
{
    /**
     * Applies committed migrations without resetting persisted records.
     * @param database The service-owned context.
     * @param seedExamples Whether to insert missing shared demo records.
     * @param cancellationToken Cancels initialization.
     * @return Completion after durable initialization.
     */
    public static async Task InitializeAsync(AttractionsDbContext database, bool seedExamples, CancellationToken cancellationToken = default)
    {
        await database.Database.MigrateAsync(cancellationToken);
        if (!seedExamples || await database.Attractions.AnyAsync(cancellationToken)) return;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var repository = new EfAttractionRepository(database);
        foreach (var attraction in AttractionSeedData.Create())
        {
            attraction.IsShared = true;
            await repository.AddAsync(attraction, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    /**
     * Reads the largest app-issued key when the single-instance allocator first starts.
     * @param factory The independent context factory.
     * @param cancellationToken Cancels database startup lookup.
     * @return The highest persisted key, or zero for an empty store.
     */
    public static async Task<long> HighestKeyAsync(IDbContextFactory<AttractionsDbContext> factory, CancellationToken cancellationToken = default)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        return await database.Attractions.MaxAsync(row => (long?)row.Id, cancellationToken) ?? 0;
    }
}
