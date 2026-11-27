using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Data.SeedData;

namespace TravelTracker.Transport.Data;

/**
 * Applies migrations and optional stable sample data to this service only.
 * Seeding checks for empty tables before inserting, so it assumes a single starting instance.
 */
public static class TransportDatabase
{
    /**
     * Migrates and seeds only empty resource tables without resetting saved data.
     * @param database Service-owned context.
     * @param seedExamples Enables first-run sample records.
     * @param cancellationToken Cancels initialization.
     * @return The number of sample records inserted; zero when seeding is off or the tables already had rows.
     */
    public static async Task<int> InitializeAsync(TransportDbContext database, bool seedExamples, CancellationToken cancellationToken = default)
    {
        await database.Database.MigrateWithoutFirstRunNoiseAsync(cancellationToken);
        if (!seedExamples) return 0;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var modeRepository = new EfTransportModeRepository(database);
        var pointRepository = new EfRoutePointRepository(database);
        var inserted = 0;
        if (!await database.TransportModes.AnyAsync(cancellationToken))
            foreach (var record in TransportSeedData.Transports())
            {
                await modeRepository.AddAsync(record, cancellationToken);
                inserted++;
            }
        if (!await database.RoutePoints.AnyAsync(cancellationToken))
            foreach (var record in TransportSeedData.Points())
            {
                await pointRepository.AddAsync(record, ownerId: null, cancellationToken);
                inserted++;
            }
        await transaction.CommitAsync(cancellationToken);
        return inserted;
    }

    /**
     * Rebuilds the single-process allocator from both persisted tables.
     * @param factory Context factory independent of request scopes.
     * @param cancellationToken Cancels lookup.
     * @return Largest stored internal key, or zero.
     */
    public static async Task<long> HighestKeyAsync(IDbContextFactory<TransportDbContext> factory, CancellationToken cancellationToken = default)
    {
        await using var database = await factory.CreateDbContextAsync(cancellationToken);
        return Math.Max(await database.TransportModes.MaxAsync(row => (long?)row.Id, cancellationToken) ?? 0,
            await database.RoutePoints.MaxAsync(row => (long?)row.Id, cancellationToken) ?? 0);
    }
}
