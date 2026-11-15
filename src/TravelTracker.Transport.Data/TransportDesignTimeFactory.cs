using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Transport.Data;

/** Creates migration models without starting the web host or contacting Users. */
public sealed class TransportDesignTimeFactory : IDesignTimeDbContextFactory<TransportDbContext>
{
    /**
     * Creates the provider-specific migration context.
     * @param _ EF tooling arguments, unused.
     * @return A PostgreSQL context built from the ConnectionStrings__Transport environment variable.
     * @throws InvalidOperationException When the environment variable is missing.
     */
    public TransportDbContext CreateDbContext(string[] _) => new(new DbContextOptionsBuilder<TransportDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Transport")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Transport before running EF tooling.")).Options);
}
