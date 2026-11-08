using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TravelTracker.Settings.Data;

/** creates migration metadata without starting the HTTP host. */
public sealed class SettingsDesignTimeFactory : IDesignTimeDbContextFactory<SettingsDbContext>
{
    /**
     * creates a PostgreSQL context for migration tooling.
     * @param args EF tooling arguments.
     * @return context using an explicit deployment connection or a non-secret tooling default.
     */
    public SettingsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<SettingsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Settings") ?? "Host=localhost;Database=traveltracker_settings").Options);
}
