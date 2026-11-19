using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Attractions.Data;

/**
 * Creates migration models without starting the web host or contacting Users.
 */
public sealed class AttractionsDesignTimeFactory : IDesignTimeDbContextFactory<AttractionsDbContext>
{
    /**
     * Creates the provider-specific migration context.
     * @param _arguments EF tooling arguments, unused.
     * @return A PostgreSQL context; tooling must supply a connection for database updates.
     */
    public AttractionsDbContext CreateDbContext(string[] _arguments) => new(new DbContextOptionsBuilder<AttractionsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Attractions") ?? "Host=localhost;Database=traveltracker_attractions").Options);
}
