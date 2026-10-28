using Microsoft.EntityFrameworkCore;
using TravelTracker.Attractions.Data.Configurations;
using TravelTracker.Attractions.Data.Entities;

namespace TravelTracker.Attractions.Data;

/**
 * Owns only the Attractions database model.
 * @param options PostgreSQL connection and EF configuration.
 */
public sealed class AttractionsDbContext(DbContextOptions<AttractionsDbContext> options) : DbContext(options)
{
    internal DbSet<AttractionRow> Attractions => Set<AttractionRow>();

    /**
     * Applies the owning service's mappings and database invariants.
     * @param modelBuilder EF model composition.
     */
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfiguration(new AttractionConfiguration());
}
