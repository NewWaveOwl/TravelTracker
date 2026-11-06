using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Accommodations.Data;

/** Creates migration metadata without starting the HTTP host. */
public sealed class AccommodationsDesignTimeFactory : IDesignTimeDbContextFactory<AccommodationsDbContext>
{
    /**
     * Creates a PostgreSQL context for migration tooling.
     * @param args EF tooling arguments.
     * @return Context using an explicit deployment connection or a non-secret tooling default.
     */
    public AccommodationsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AccommodationsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Accommodations") ?? "Host=localhost;Database=traveltracker_accommodations").Options);
}
