using Microsoft.EntityFrameworkCore;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Repositories;

/**
 * Stores trips in the service-owned PostgreSQL database. A save writes the trip, its legs and their snapshots in
 * one explicit transaction, so a failure at any row leaves nothing behind.
 *
 * @param database The Trips database context for the current request.
 */
public sealed class EfTripRepository(TripsDbContext database) : ITripRepository
{
    /**
     * Commits the trip row and every leg row together, or commits nothing.
     *
     * @param trip The validated trip to store.
     * @param ownerIssuer The validated token issuer of the owner.
     * @param ownerSubject The validated token subject of the owner.
     * @param cancellationToken Cancels database I/O.
     * @return Completion after the transaction commits.
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
     * Replaces the owned trip and itinerary in one transaction. The revision check and increment
     * are part of the parent update, before the old leg rows are touched.
     *
     * @param trip The validated replacement with the existing trip identifier.
     * @param ownerIssuer The caller's validated issuer.
     * @param ownerSubject The caller's validated subject.
     * @param cancellationToken Cancels database I/O.
     * @param expectedRevision Revision named by the client.
     * @return Whether the replacement committed, the owned trip was absent, or its revision had changed.
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

    /** {@inheritDoc} */
    public Task<long?> GetOwnedRevisionAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default) =>
        database.Trips.Where(row => row.TripId == tripId && row.OwnerIssuer == ownerIssuer && row.OwnerSubject == ownerSubject)
            .Select(row => (long?)row.Revision).SingleOrDefaultAsync(cancellationToken);

    /**
     * Loads a trip with its legs in sequence order, only when the caller owns it.
     *
     * @param tripId The public trip identifier.
     * @param ownerIssuer The caller's validated issuer.
     * @param ownerSubject The caller's validated subject.
     * @param cancellationToken Cancels database I/O.
     * @return The detached trip, or null when absent or owned by someone else.
     */
    public async Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        var row = await database.Trips.AsNoTracking()
            .Include(trip => trip.Legs)
            .SingleOrDefaultAsync(trip => trip.TripId == tripId && trip.OwnerIssuer == ownerIssuer && trip.OwnerSubject == ownerSubject,
                cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /**
     * Maps a stored trip and its legs back to the domain, restoring each offset the traveller entered.
     *
     * @param row The stored trip with its legs loaded.
     * @return The rebuilt trip, which is validated again by the domain.
     */
    private static Trip ToDomain(TripRow row) => Trip.Create(
        row.Name,
        TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId),
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
     * Maps a validated trip, its owner and its snapshots to persistence rows. Instants are written in UTC because
     * Npgsql refuses a non-zero offset for timestamptz; the entered offsets are stored beside them.
     *
     * @param trip The validated trip.
     * @param ownerIssuer The owner's issuer.
     * @param ownerSubject The owner's subject.
     * @return A detached trip row with its leg rows attached.
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
