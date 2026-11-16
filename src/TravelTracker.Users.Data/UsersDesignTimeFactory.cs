using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Users.Data;

/** Creates migration metadata without starting the HTTP host or contacting an identity provider. */
public sealed class UsersDesignTimeFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    /**
     * Creates a PostgreSQL context for migration tooling.
     * @param _arguments EF tooling arguments, unused.
     * @return Context using an explicit deployment connection or a non-secret tooling default.
     */
    public UsersDbContext CreateDbContext(string[] _arguments) => new(new DbContextOptionsBuilder<UsersDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Users") ?? "Host=localhost;Database=traveltracker_users").Options);
}
