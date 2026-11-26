using System.Data;
using Microsoft.EntityFrameworkCore;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Data.Entities;

namespace TravelTracker.Trips.Data.Repositories;

/**
 * Stores trips in the service-owned PostgreSQL database. A save writes the trip, stops, legs and snapshots in
 * one explicit transaction, so a failure at any row leaves nothing behind.
 *
 * @param database the Trips database context for the current request.
 */
public sealed class EfTripRepository(TripsDbContext database) : ITripRepository
{
    /**
     * Commits the trip row, stop snapshots and leg rows together, or commits nothing.
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
     * Replaces the owned trip and itinerary in one transaction. The revision check and increment
     * are part of the parent update, before the old stop or leg rows are touched.
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
            await database.TripStops.Where(stop => stop.TripId == trip.TripId).ExecuteDeleteAsync(cancellationToken);
            database.TripLegs.AddRange(replacement.Legs);
            database.TripStops.AddRange(replacement.Stops);
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
     * Loads an owned trip and its ordered snapshots without multiplying sibling collection rows.
     * A repeatable-read transaction keeps the split queries on the same snapshot if a replacement commits between them.
     *
     * @param tripId the public trip identifier.
     * @param ownerIssuer the caller's validated issuer.
     * @param ownerSubject the caller's validated subject.
     * @param cancellationToken cancels database I/O.
     * @return the detached trip, or null when absent or owned by someone else.
     */
    public async Task<Trip?> GetOwnedByIdAsync(Guid tripId, string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var row = await database.Trips.AsNoTracking()
            .Include(trip => trip.Legs)
            .Include(trip => trip.Stops)
            .AsSplitQuery()
            .SingleOrDefaultAsync(trip => trip.TripId == tripId && trip.OwnerIssuer == ownerIssuer && trip.OwnerSubject == ownerSubject,
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return row is null ? null : ToDomain(row);
    }

    /** {@inheritDoc} */
    public async Task<bool> SetOwnedStatusAsync(Guid tripId, string ownerIssuer, string ownerSubject, string? status, CancellationToken cancellationToken) =>
        await database.Trips.Where(row => row.TripId == tripId && row.OwnerIssuer == ownerIssuer && row.OwnerSubject == ownerSubject)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, status), cancellationToken) == 1;

    /** {@inheritDoc} */
    public async Task<IReadOnlyList<TripHistoryEntry>> ListOwnedAsync(string ownerIssuer, string ownerSubject, CancellationToken cancellationToken = default)
    {
        var rows = await database.Trips.AsNoTracking()
            .Where(row => row.OwnerIssuer == ownerIssuer && row.OwnerSubject == ownerSubject)
            .OrderByDescending(row => row.StartAt)
            .ThenBy(row => row.TripId)
            .Select(row => new { row.TripId, row.Name, row.StartAt, row.StartOffset, row.EndAt, row.EndOffset, row.Status })
            .ToListAsync(cancellationToken);
        return rows.Select(row => new TripHistoryEntry(row.TripId, row.Name,
            row.StartAt.ToOffset(row.StartOffset), row.EndAt.ToOffset(row.EndOffset), row.Status)).ToArray();
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
     * Restores a stored trip with its entered offsets and ordered stops. Trips written before the stop migration
     * derive their stop order from existing leg snapshots without another catalogue lookup.
     *
     * @param row The stored trip with its legs and stops loaded.
     * @return The rebuilt trip, which is validated again by the domain.
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
            leg.ArrivalAt.ToOffset(leg.ArrivalOffset), leg.EncodedPolyline)),
        row.TripId, row.Revision,
        row.Stops.Count == 0 ? null : row.Stops.OrderBy(stop => stop.Sequence)
            .Select(stop => new TripPlace(stop.PlaceId, stop.PlaceType, stop.PlaceName, stop.PlaceAddress, stop.Latitude, stop.Longitude)));

    /**
     * maps a validated trip, its owner and its snapshots to persistence rows. Instants are written in UTC because
     * Npgsql refuses a non-zero offset for timestamptz; the entered offsets are stored beside them.
     *
     * @param trip The validated trip.
     * @param ownerIssuer The owner's issuer.
     * @param ownerSubject The owner's subject.
     * @return A detached trip row with its stop and leg rows attached.
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
        Stops = trip.Stops.Select((stop, index) => new TripStopRow
        {
            TripId = trip.TripId,
            Sequence = index + 1,
            PlaceId = stop.PlaceId,
            PlaceType = stop.Type,
            PlaceName = stop.Name,
            PlaceAddress = stop.Address,
            Latitude = stop.Latitude,
            Longitude = stop.Longitude
        }).ToList(),
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
            ArrivalOffset = leg.Arrival.Offset,
            EncodedPolyline = leg.EncodedPolyline
        }).ToList()
    };
}
