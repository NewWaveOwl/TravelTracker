using Microsoft.EntityFrameworkCore;
using TravelTracker.Settings.Data.Configurations;
using TravelTracker.Settings.Data.Entities;

namespace TravelTracker.Settings.Data;

/**
 * Owns only the Settings database model.
 * @param options PostgreSQL connection and EF configuration.
 */
public sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options) : DbContext(options)
{
    internal DbSet<SettingsRow> UserSettings => Set<SettingsRow>();

    /**
     * Applies the service-owned mapping and database invariants.
     * @param modelBuilder EF model composition.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new SettingsConfiguration());
}
