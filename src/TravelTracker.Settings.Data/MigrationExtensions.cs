using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace TravelTracker.Settings.Data;

/**
 * Shared migration entry point for the service databases.
 */
internal static class MigrationExtensions
{
    /**
     * Applies pending migrations after creating an empty history table when it is missing.
     * EF reads that table first and, on a fresh database, logs the failed read as an error before it creates the table; creating it
     * up front keeps that expected failure out of the log, while every other failure still surfaces unchanged.
     * @param database The service-owned database facade.
     * @param cancellationToken Cancels the migration.
     * @return Completion once all migrations are applied.
     */
    public static async Task MigrateWithoutFirstRunNoiseAsync(this DatabaseFacade database, CancellationToken cancellationToken = default)
    {
        // same shape EF creates for Npgsql, so Migrate adopts it as if it had made it
        if (await database.CanConnectAsync(cancellationToken))
            await database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
                );
                """, cancellationToken);
        await database.MigrateAsync(cancellationToken);
    }
}
