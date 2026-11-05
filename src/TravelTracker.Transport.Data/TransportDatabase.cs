using Microsoft.EntityFrameworkCore;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Data.SeedData;

namespace TravelTracker.Transport.Data;

/** Applies migrations and optional stable sample data to this service only. */
public static class TransportDatabase
{
    /** Migrates and seeds only empty resource tables without resetting saved data.
     * @param database Service-owned context.
     * @param seedExamples Enables first-run sample records.
     * @param cancellationToken Cancels initialization.
     * @return Completion after initialization.
     */
    public static async Task InitializeAsync(TransportDbContext database, bool seedExamples, CancellationToken cancellationToken = default)
    {
        await database.Database.MigrateAsync(cancellationToken);
        if (!seedExamples) return;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        if (!await database.TransportModes.AnyAsync(cancellationToken))
            foreach (var record in TransportSeedData.Transports())
                await new EfTransportModeRepository(database).AddAsync(record, cancellationToken);
        if (!await database.RoutePoints.AnyAsync(cancellationToken))
            foreach (var record in TransportSeedData.Points())
                await new EfRoutePointRepository(database).AddAsync(record, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /** Rebuilds the single-process allocator from both persisted tables.
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
