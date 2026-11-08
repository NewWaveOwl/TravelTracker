using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Trips.Data;

/** Creates migration metadata without starting the HTTP host. */
public sealed class TripsDesignTimeFactory : IDesignTimeDbContextFactory<TripsDbContext>
{
    /**
     * Creates a PostgreSQL context for migration tooling.
     *
     * @param args EF tooling arguments.
     * @return Context using an explicit deployment connection or a non-secret tooling default.
     */
    public TripsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TripsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Trips") ?? "Host=localhost;Database=traveltracker_trips").Options);
}
