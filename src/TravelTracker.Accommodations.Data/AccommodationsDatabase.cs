using Microsoft.EntityFrameworkCore;

namespace TravelTracker.Accommodations.Data;

/** Applies the service's committed schema changes without resetting catalogue data. */
public static class AccommodationsDatabase
{
    /**
     * Applies all pending migrations to the service-owned database.
     * @param database The configured Accommodations context.
     * @param cancellationToken Cancels database initialization.
     * @return Completion after migrations are applied.
     */
    public static Task InitializeAsync(AccommodationsDbContext database, CancellationToken cancellationToken = default) =>
        database.Database.MigrateAsync(cancellationToken);
}
