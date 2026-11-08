using Microsoft.EntityFrameworkCore;

namespace TravelTracker.Settings.Data;

/** applies the service's committed schema changes without touching saved preferences. */
public static class SettingsDatabase
{
    /**
     * applies all pending migrations to the service-owned database.
     * @param database the configured Settings context.
     * @param cancellationToken cancels database initialization.
     * @return completion after migrations are applied.
     */
    public static Task InitializeAsync(SettingsDbContext database, CancellationToken cancellationToken = default) =>
        database.Database.MigrateAsync(cancellationToken);
}
