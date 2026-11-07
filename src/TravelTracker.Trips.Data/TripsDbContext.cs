using Microsoft.EntityFrameworkCore;
using TravelTracker.Trips.Data.Configurations;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data;

/**
 * Owns the Trips service database model: one trip row, its ordered leg rows and their place and transport snapshots.
 *
 * @param options PostgreSQL connection and EF configuration.
 */
public sealed class TripsDbContext(DbContextOptions<TripsDbContext> options) : DbContext(options)
{
    internal DbSet<TripRow> Trips => Set<TripRow>();
    internal DbSet<TripLegRow> TripLegs => Set<TripLegRow>();

    /**
     * Applies the service-owned trip and leg mappings.
     *
     * @param modelBuilder EF model composition.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new TripConfiguration());
        modelBuilder.ApplyConfiguration(new TripLegConfiguration());
    }
}
