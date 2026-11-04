using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Users.Data;

/** Creates migration metadata without starting the HTTP host or contacting an identity provider. */
public sealed class UsersDesignTimeFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    /**
     * Creates a PostgreSQL context for migration tooling.
     * @param args EF tooling arguments.
     * @return Context using an explicit deployment connection or a non-secret tooling default.
     */
    public UsersDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<UsersDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Users") ?? "Host=localhost;Database=traveltracker_users").Options);
}
