using Microsoft.EntityFrameworkCore;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Repositories;

/**
 * stores trips in the service-owned PostgreSQL database. A save writes the trip, its legs and their snapshots in
 * one explicit transaction, so a failure at any row leaves nothing behind.
 *
 * @param database the Trips database context for the current request.
 */
public sealed class EfTripRepository(TripsDbContext database) : ITripRepository
{
    /**
     * commits the trip row and every leg row together, or commits nothing.
     *
     * @param trip the validated trip to store.
     * @param ownerIssuer the validated token issuer of the owner.
     * @param ownerSubject the validated token subject of the owner.
     * @param cancellationToken cancels database I/O.
     * @return completion after the transaction commits.
     */
    public async Task AddAsync(Trip trip, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            database.Trips.Add(ToRow(trip, ownerIssuer, ownerSubject));
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    /**
     * replaces the owned trip and itinerary in one transaction. The revision check and increment
     * are part of the parent update, before the old leg rows are touched.
     *
     * @param trip the validated replacement with the existing trip identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param expectedRevision revision named by the client.
     * @param cancellationToken cancels database I/O.
     * @return whether the replacement committed, the owned trip was absent, or its revision had changed.
     */
    public async Task<TripReplacementStatus> ReplaceOwnedAsync(Trip trip, string ownerIssuer, string ownerSubject, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var replacement = ToRow(trip, ownerIssuer, ownerSubject);
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var updated = await database.Trips
                .Where(row => row.TripId == trip.TripId && row.OwnerIssuer == ownerIssuer &&
                    row.OwnerSubject == ownerSubject && row.Revision == expectedRevision)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(row => row.Revision, row => row.Revision + 1)
                    .SetProperty(row => row.Name, replacement.Name)
                    .SetProperty(row => row.TimeZoneId, replacement.TimeZoneId)
                    .SetProperty(row => row.StartAt, replacement.StartAt)
                    .SetProperty(row => row.StartOffset, replacement.StartOffset)
                    .SetProperty(row => row.EndAt, replacement.EndAt)
                    .SetProperty(row => row.EndOffset, replacement.EndOffset)
                    .SetProperty(row => row.IsRoundTrip, replacement.IsRoundTrip), cancellationToken);
            if (updated == 0)
                return await GetOwnedRevisionAsync(trip.TripId, ownerIssuer, ownerSubject, cancellationToken) is null
                    ? TripReplacementStatus.NotFound : TripReplacementStatus.Stale;

            await database.TripLegs.Where(leg => leg.TripId == trip.TripId).ExecuteDeleteAsync(cancellationToken);
            database.TripLegs.AddRange(replacement.Legs);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return TripReplacementStatus.Replaced;
        }
        finally
        {
            database.ChangeTracker.Clear();
        }
    }

    /**
     * reads the revision of an owned trip without loading its legs.
     *
     * @param tripId public trip identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param cancellationToken cancels database I/O.
     * @return the current revision, or null when the trip is absent or owned by someone else.
     */
    public Task<long?> GetOwnedRevisionAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default) =>
        database.Trips.Where(row => row.TripId == tripId && row.OwnerIssuer == ownerIssuer && row.OwnerSubject == ownerSubject)
            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(cancellationToken);

    /**
     * loads a trip with its legs in sequence order, only when the caller owns it.
     *
     * @param tripId the public trip identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param cancellationToken cancels database I/O.
     * @return the detached trip, or null when absent or owned by someone else.
     */
    public async Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        var row = await database.Trips.AsNoTracking()
            .Include(trip => trip.Legs)
            .SingleOrDefaultAsync(trip => trip.TripId == tripId && trip.OwnerIssuer == ownerIssuer && trip.OwnerSubject == ownerSubject,
                cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /** {@inheritDoc} */
    public async Task<IReadOnlyList<TripHistoryEntry>> ListOwnedAsync(string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        var rows = await database.Trips.AsNoTracking()
            .Where(row => row.OwnerIssuer == ownerIssuer && row.OwnerSubject == ownerSubject)
            .OrderByDescending(row => row.StartAt)
            .ThenBy(row => row.TripId)
            .Select(row => new { row.TripId, row.Name, row.StartAt, row.StartOffset, row.EndAt, row.EndOffset })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new TripHistoryEntry(row.TripId, row.Name,
            row.StartAt.ToOffset(row.StartOffset), row.EndAt.ToOffset(row.EndOffset))).ToArray();
    }

    /**
     * resolves the stored zone id, naming only the zone id when this host's time zone database does not know it so the failure is diagnosable.
     *
     * @param row the stored trip.
     * @return the time zone.
     * @throws InvalidOperationException when the stored zone id is unknown to this host.
     */
    private static TimeZoneInfo ResolveStoredZone(TripRow row)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"stored trip has time zone id '{row.TimeZoneId}', which this host does not know.", exception);
        }
    }

    /**
     * maps a stored trip and its legs back to the domain, restoring each offset the traveller entered.
     *
     * @param row the stored trip with its legs loaded.
     * @return the rebuilt trip, which is validated again by the domain.
     */
    private static Trip ToDomain(TripRow row) => Trip.Create(
        row.Name,
        ResolveStoredZone(row),
        row.StartAt.ToOffset(row.StartOffset),
        row.EndAt.ToOffset(row.EndOffset),
        row.IsRoundTrip,
        row.Legs.OrderBy(leg => leg.Sequence).Select(leg => new TripLeg(
            leg.Sequence,
            new TripPlace(leg.FromPlaceId, leg.FromPlaceType, leg.FromPlaceName, leg.FromPlaceAddress, leg.FromLatitude, leg.FromLongitude),
            new TripPlace(leg.ToPlaceId, leg.ToPlaceType, leg.ToPlaceName, leg.ToPlaceAddress, leg.ToLatitude, leg.ToLongitude),
            new TripTransport(leg.TransportModeId, leg.TransportCode, leg.TransportName),
            leg.DepartureAt.ToOffset(leg.DepartureOffset),
            leg.ArrivalAt.ToOffset(leg.ArrivalOffset))),
        row.TripId, row.Revision);

    /**
     * maps a validated trip, its owner and its snapshots to persistence rows. Instants are written in UTC because
     * Npgsql refuses a non-zero offset for timestamptz; the entered offsets are stored beside them.
     *
     * @param trip the validated trip.
     * @param ownerIssuer the owner's issuer.
     * @param ownerSubject the owner's subject.
     * @return a detached trip row with its leg rows attached.
     */
    private static TripRow ToRow(Trip trip, string ownerIssuer, string ownerSubject) => new()
    {
        TripId = trip.TripId,
        Revision = trip.Revision,
        OwnerIssuer = ownerIssuer,
        OwnerSubject = ownerSubject,
        Name = trip.Name,
        TimeZoneId = trip.TimeZone.Id,
        StartAt = trip.Start.ToUniversalTime(),
        StartOffset = trip.Start.Offset,
        EndAt = trip.End.ToUniversalTime(),
        EndOffset = trip.End.Offset,
        IsRoundTrip = trip.IsRoundTrip,
        Legs = trip.Legs.Select(leg => new TripLegRow
        {
            TripId = trip.TripId,
            Sequence = leg.Sequence,
            FromPlaceId = leg.From.PlaceId,
            FromPlaceType = leg.From.Type,
            FromPlaceName = leg.From.Name,
            FromPlaceAddress = leg.From.Address,
            FromLatitude = leg.From.Latitude,
            FromLongitude = leg.From.Longitude,
            ToPlaceId = leg.To.PlaceId,
            ToPlaceType = leg.To.Type,
            ToPlaceName = leg.To.Name,
            ToPlaceAddress = leg.To.Address,
            ToLatitude = leg.To.Latitude,
            ToLongitude = leg.To.Longitude,
            TransportModeId = leg.Transport.TransportModeId,
            TransportCode = leg.Transport.Code,
            TransportName = leg.Transport.Name,
            DepartureAt = leg.Departure.ToUniversalTime(),
            DepartureOffset = leg.Departure.Offset,
            ArrivalAt = leg.Arrival.ToUniversalTime(),
            ArrivalOffset = leg.Arrival.Offset
        }).ToList()
    };
}
