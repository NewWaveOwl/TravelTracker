using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Settings.Data.Entities;
using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Data.Repositories;

/**
 * stores preferences only in the service-owned PostgreSQL database.
 * @param database the request-scoped context.
 */
public sealed class EfSettingsRepository(SettingsDbContext database) : ISettingsRepository
{
    /**
     * reads a detached copy so callers cannot change tracked rows.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param cancellationToken cancels database I/O.
     * @return the saved values, or null when none were saved.
     */
    public async Task<UserSettings?> GetByOwnerAsync(string issuer, string subject, CancellationToken cancellationToken = default)
    {
        var row = await database.UserSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.OwnerIssuer == issuer && row.OwnerSubject == subject, cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * checks the revision, then writes. the revision is a concurrency token, so the UPDATE only applies to the row version that was checked.
     * a first save inserts row revision 1; a racing insert that hits the unique key is reported as stale.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param settings the complete accepted values to store.
     * @param expectedRevision the revision the caller last read.
     * @param cancellationToken cancels database I/O.
     * @return the stored values with their new revision, or null when the revision was stale.
     */
    public async Task<UserSettings?> ReplaceIfRevisionAsync(string issuer, string subject, UserSettings settings, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var row = await database.UserSettings
            .SingleOrDefaultAsync(row => row.OwnerIssuer == issuer && row.OwnerSubject == subject, cancellationToken);
        if (row is null)
        {
            if (expectedRevision != 0) return null;
            var inserted = ToRow(issuer, subject, settings, revision: 1);
            database.UserSettings.Add(inserted);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                database.ChangeTracker.Clear();
                return null;
            }
            database.ChangeTracker.Clear();
            return ToDomain(inserted);
        }

        if (expectedRevision == 0 || row.Revision != expectedRevision)
        {
            database.ChangeTracker.Clear();
            return null;
        }

        row.Theme = settings.Theme;
        row.DefaultMapLatitude = settings.DefaultMapLatitude;
        row.DefaultMapLongitude = settings.DefaultMapLongitude;
        row.DefaultMapZoom = settings.DefaultMapZoom;
        row.PreferredTransportMode = settings.PreferredTransportMode;
        row.TimeZone = settings.TimeZone;
        row.Revision = expectedRevision + 1;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            database.ChangeTracker.Clear();
            return null;
        }
        database.ChangeTracker.Clear();
        return ToDomain(row);
    }

    /**
     * converts a stored row into the domain values.
     * @param row the persistence row.
     * @return the domain representation without owner identity.
     */
    private static UserSettings ToDomain(SettingsRow row) => new()
    {
        Theme = row.Theme,
        DefaultMapLatitude = row.DefaultMapLatitude,
        DefaultMapLongitude = row.DefaultMapLongitude,
        DefaultMapZoom = row.DefaultMapZoom,
        PreferredTransportMode = row.PreferredTransportMode,
        TimeZone = row.TimeZone,
        Revision = row.Revision
    };

    /**
     * converts accepted values and the server-resolved owner into a new row.
     * @param issuer the validated token issuer.
     * @param subject the validated token subject.
     * @param settings the accepted values.
     * @param revision the revision the new row starts at.
     * @return a detached database row.
     */
    private static SettingsRow ToRow(string issuer, string subject, UserSettings settings, long revision) => new()
    {
        OwnerIssuer = issuer,
        OwnerSubject = subject,
        Theme = settings.Theme,
        DefaultMapLatitude = settings.DefaultMapLatitude,
        DefaultMapLongitude = settings.DefaultMapLongitude,
        DefaultMapZoom = settings.DefaultMapZoom,
        PreferredTransportMode = settings.PreferredTransportMode,
        TimeZone = settings.TimeZone,
        Revision = revision
    };
}
