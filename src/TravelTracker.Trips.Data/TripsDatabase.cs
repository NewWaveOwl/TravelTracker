using Microsoft.EntityFrameworkCore;

namespace TravelTracker.Trips.Data;

/** Applies the Trips service's committed schema changes without resetting stored trips. */
public static class TripsDatabase
{
    /**
     * Applies all pending migrations to the service-owned database.
     *
     * @param database The configured Trips context.
     * @param cancellationToken Cancels database initialization.
     * @return Completion after migrations are applied.
     */
    public static Task InitializeAsync(TripsDbContext database, CancellationToken cancellationToken = default) =>
        database.Database.MigrateAsync(cancellationToken);
}
