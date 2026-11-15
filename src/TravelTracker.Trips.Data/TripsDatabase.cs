using Microsoft.EntityFrameworkCore;

namespace TravelTracker.Trips.Data;

/** applies the Trips service's committed schema changes without resetting stored trips. */
public static class TripsDatabase
{
    /**
     * applies all pending migrations to the service-owned database.
     *
     * @param database the configured Trips context.
     * @param cancellationToken cancels database initialization.
     * @return completion after migrations are applied.
     */
    public static Task InitializeAsync(TripsDbContext database, CancellationToken cancellationToken = default) =>
        database.Database.MigrateAsync(cancellationToken);
}
