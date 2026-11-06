using Microsoft.EntityFrameworkCore;
using TravelTracker.Accommodations.Data.Entities;
using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Data.Repositories;

/** Loads and stores accommodations in the service-owned PostgreSQL database. */
public sealed class EfAccommodationRepository(AccommodationsDbContext database) : IAccommodationRepository
{
    /**
     * Applies private/shared visibility predicates in PostgreSQL before materializing records.
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param includeShared Whether shared catalogue entries are visible.
     * @param cancellationToken Cancels database I/O.
     * @return Detached records visible to the caller.
     */
    public async Task<List<Accommodation>> GetVisibleAsync(string issuer, string subject, bool includeShared,
        CancellationToken cancellationToken = default) =>
        (await database.Accommodations.AsNoTracking().Where(row =>
            row.IsShared && includeShared || !row.IsShared && row.OwnerIssuer == issuer && row.OwnerSubject == subject)
            .ToListAsync(cancellationToken)).Select(ToDomain).ToList();

    /**
     * Retrieves one public identifier only when it is visible to the caller.
     * @param accommodationId Public accommodation identifier.
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param includeShared Whether shared catalogue entries are visible.
     * @param cancellationToken Cancels database I/O.
     * @return The detached record, or null when absent or private to someone else.
     */
    public async Task<Accommodation?> GetVisibleByIdAsync(Guid accommodationId, string issuer, string subject,
        bool includeShared, CancellationToken cancellationToken = default)
    {
        var row = await database.Accommodations.AsNoTracking().SingleOrDefaultAsync(row => row.AccommodationId == accommodationId &&
            (row.IsShared && includeShared || !row.IsShared && row.OwnerIssuer == issuer && row.OwnerSubject == subject),
            cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Adds a detached row and clears tracked state after saving.
     * @param accommodation The accepted domain record.
     * @param cancellationToken Cancels database I/O.
     * @return Completion after saving.
     */
    public async Task AddAsync(Accommodation accommodation, CancellationToken cancellationToken = default)
    {
        database.Accommodations.Add(ToRow(accommodation));
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
    }

    /**
     * Maps a persistence row to the service's domain model.
     * @param row Stored accommodation row.
     * @return Domain accommodation with preserved owner and visibility.
     */
    private static Accommodation ToDomain(AccommodationRow row) => new()
    {
        AccommodationId = row.AccommodationId,
        Name = row.Name,
        Address = row.Address,
        Latitude = row.Latitude,
        Longitude = row.Longitude,
        OwnerIssuer = row.OwnerIssuer,
        OwnerSubject = row.OwnerSubject,
        IsShared = row.IsShared
    };

    /**
     * Maps the validated domain model to a persistence row.
     * @param record Accepted accommodation.
     * @return Detached PostgreSQL row.
     */
    private static AccommodationRow ToRow(Accommodation record) => new()
    {
        AccommodationId = record.AccommodationId,
        Name = record.Name,
        Address = record.Address,
        Latitude = record.Latitude,
        Longitude = record.Longitude,
        OwnerIssuer = record.OwnerIssuer,
        OwnerSubject = record.OwnerSubject,
        IsShared = record.IsShared
    };
}
