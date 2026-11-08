using Microsoft.EntityFrameworkCore;
using TravelTracker.Accommodations.Data.Configurations;
using TravelTracker.Accommodations.Data.Entities;

namespace TravelTracker.Accommodations.Data;

/**
 * Owns the Accommodations service database model.
 * @param options PostgreSQL connection and EF configuration.
 */
public sealed class AccommodationsDbContext(DbContextOptions<AccommodationsDbContext> options) : DbContext(options)
{
    internal DbSet<AccommodationRow> Accommodations => Set<AccommodationRow>();

    /**
     * Applies the service-owned accommodation mapping.
     * @param modelBuilder EF model composition.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new AccommodationConfiguration());
}
