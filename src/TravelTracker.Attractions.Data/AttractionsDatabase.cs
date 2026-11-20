using Microsoft.EntityFrameworkCore;
using TravelTracker.Data;
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
     * @param seedExamples Whether to insert the shared demo records, only when the table is empty.
     * @param cancellationToken Cancels initialization.
     * @return The number of example attractions inserted; zero when seeding is off or the table already had rows.
     */
    public static async Task<int> InitializeAsync(AttractionsDbContext database, bool seedExamples, CancellationToken cancellationToken = default)
    {
        await database.Database.MigrateWithoutFirstRunNoiseAsync(cancellationToken);
        if (!seedExamples || await database.Attractions.AnyAsync(cancellationToken)) return 0;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var repository = new EfAttractionRepository(database);
        var inserted = 0;
        foreach (var attraction in AttractionSeedData.Create())
        {
            attraction.IsShared = true;
            await repository.AddAsync(attraction, cancellationToken);
            inserted++;
        }
        await transaction.CommitAsync(cancellationToken);
        return inserted;
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
