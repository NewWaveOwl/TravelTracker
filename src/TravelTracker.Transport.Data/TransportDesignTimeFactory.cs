using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Transport.Data;

/**

 * Creates migration models without starting the web host or contacting Users.

 */
public sealed class TransportDesignTimeFactory : IDesignTimeDbContextFactory<TransportDbContext>
{
    /**
     * Creates the provider-specific migration context.
     * @param args EF tooling arguments.
     * @return A PostgreSQL context; tooling must supply a connection for database updates.
     */
    public TransportDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TransportDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Transport") ?? "Host=localhost;Database=traveltracker_attractions").Options);
}
