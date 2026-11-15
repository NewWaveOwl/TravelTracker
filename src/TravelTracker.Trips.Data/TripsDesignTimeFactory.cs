using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Trips.Data;

/** creates migration metadata without starting the HTTP host. */
public sealed class TripsDesignTimeFactory : IDesignTimeDbContextFactory<TripsDbContext>
{
    /**
     * creates a PostgreSQL context for migration tooling.
     *
     * @param _arguments EF tooling arguments, unused.
     * @return context using an explicit deployment connection or a non-secret tooling default.
     */
    public TripsDbContext CreateDbContext(string[] _arguments) => new(new DbContextOptionsBuilder<TripsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Trips") ?? "Host=localhost;Database=traveltracker_trips").Options);
}
