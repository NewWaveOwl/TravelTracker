using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Data;
using TravelTracker.Trips.Data.Repositories;
using TravelTracker.Trips.Data.Tests.Support;

namespace TravelTracker.Trips.Data.Tests;

/**
 * Verifies PostgreSQL migration, atomic trip saves, owner scoping and restart behavior against a disposable database.
 */
[Trait("Category", "PostgreSQL")]
public sealed class TripRepositoryPostgreSqlTests
{
    private const string OwnerIssuer = "https://issuer.example/";
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task SavedTrip_SurvivesContextRestart_WithSnapshotsAndEnteredOffsets()
    {
        await using var database = new PostgresTestDatabase();
        var expected = SampleTrip();
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            await new EfTripRepository(context).AddAsync(expected, OwnerIssuer, "alice");
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var repository = new EfTripRepository(restarted);
        var persisted = await repository.GetOwnedByIdAsync(expected.TripId, OwnerIssuer, "alice");
        var history = Assert.Single(await repository.ListOwnedAsync(OwnerIssuer, "alice"));
        Assert.Equal(expected.TripId, history.TripId);
        Assert.Equal(expected.Name, history.Name);
        Assert.Equal(expected.Start.Offset, history.Start.Offset);
        Assert.Equal(expected.End.Offset, history.End.Offset);
        Assert.Empty(await repository.ListOwnedAsync(OwnerIssuer, "bob"));
        Assert.Empty(await repository.ListOwnedAsync("https://other-issuer.example/", "alice"));
        var migrations = await restarted.Database.GetAppliedMigrationsAsync();

        Assert.False(restarted.Database.HasPendingModelChanges());
        Assert.NotNull(persisted);
        Assert.Equal(expected.TripId, persisted.TripId);
        Assert.Equal("Paris weekend", persisted.Name);
        Assert.Equal("Europe/Paris", persisted.TimeZone.Id);
        Assert.Equal(expected.Start, persisted.Start);
        Assert.Equal(TimeSpan.FromHours(2), persisted.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(2), persisted.End.Offset);
        Assert.True(persisted.IsRoundTrip);
        Assert.Equal(expected.Stops, persisted.Stops);
        Assert.Equal(2, persisted.Legs.Count);
        var first = persisted.Legs[0];
        Assert.Equal(1, first.Sequence);
        Assert.Equal("Hotel Lumiere", first.From.Name);
        Assert.Equal("1 rue de Rivoli", first.From.Address);
        Assert.Equal(48.85m, first.From.Latitude);
        Assert.Equal(2.35m, first.From.Longitude);
        Assert.Equal("train", first.Transport.Code);
        Assert.Equal(TimeSpan.FromHours(2), first.Departure.Offset);
        Assert.Equal(expected.Legs[0].Departure, first.Departure);
        Assert.Equal("Louvre", persisted.Legs[1].From.Name);
        Assert.Equal("Hotel Lumiere", persisted.Legs[1].To.Name);
        Assert.Contains(migrations, migration => migration.EndsWith("_InitialTrips", StringComparison.Ordinal));
        Assert.Contains("20261109120000_TripRevisionCheck", migrations);
        Assert.Contains("20261123004432_TripStops", migrations);
    }

    [Fact]
    public async Task FailedWrite_LeavesNoTripOrLegRows()
    {
        await using var database = new PostgresTestDatabase();
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            var failing = SampleTrip(secondTransportCode: new string('x', 33));
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => new EfTripRepository(context).AddAsync(failing, OwnerIssuer, "alice"));
            var postgres = Assert.IsType<PostgresException>(exception.InnerException);
            Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, postgres.SqlState);
        }

        await using var check = CreateContext(database.ConnectionString);
        Assert.Equal(0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Trips\"").SingleAsync());
        Assert.Equal(0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripLegs\"").SingleAsync());
        Assert.Equal(0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());
    }

    [Fact]
    public async Task ReplaceOwned_ReplacesEveryLegAndKeepsPublicIdentity()
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        var replacement = Trip.Create("One-way weekend", original.TimeZone, original.Start, original.End, false,
            [original.Legs[0]], original.TripId);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            var repository = new EfTripRepository(context);
            await repository.AddAsync(original, OwnerIssuer, "alice");
            Assert.Equal(TripReplacementStatus.NotFound, await repository.ReplaceOwnedAsync(replacement, OwnerIssuer, "bob", original.Revision));
            Assert.Equal(TripReplacementStatus.Replaced, await repository.ReplaceOwnedAsync(replacement, OwnerIssuer, "alice", original.Revision));
        }

        await using var check = CreateContext(database.ConnectionString);
        var saved = await new EfTripRepository(check).GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(saved);
        Assert.Equal(original.TripId, saved.TripId);
        Assert.Equal(replacement.Name, saved.Name);
        Assert.Equal(original.Revision + 1, saved.Revision);
        Assert.False(saved.IsRoundTrip);
        Assert.Equal(replacement.Legs[0], Assert.Single(saved.Legs));
        Assert.Equal(replacement.Stops, saved.Stops);
    }

    [Fact]
    public async Task ReplaceOwned_FailedLegWriteRollsBackParentAndAllLegs()
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        var invalidLegs = SampleTrip(secondTransportCode: new string('x', 33)).Legs;
        var replacement = Trip.Create("New name", original.TimeZone, original.Start, original.End, true, invalidLegs, original.TripId);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            var repository = new EfTripRepository(context);
            await repository.AddAsync(original, OwnerIssuer, "alice");
            var exception = await Assert.ThrowsAsync<DbUpdateException>(() => repository.ReplaceOwnedAsync(replacement, OwnerIssuer, "alice", original.Revision));
            Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using var check = CreateContext(database.ConnectionString);
        var saved = await new EfTripRepository(check).GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(saved);
        Assert.Equal(original.Name, saved.Name);
        Assert.Equal(original.Revision, saved.Revision);
        Assert.Equal(original.Legs, saved.Legs);
        Assert.Equal(original.Stops, saved.Stops);
    }

    [Fact]
    public async Task ReplaceOwned_ConcurrentWritesAcceptOnlyOneCompleteItinerary()
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        await using var check = CreateContext(database.ConnectionString);
        await TripsDatabase.InitializeAsync(check);
        var repository = new EfTripRepository(check);
        await repository.AddAsync(original, OwnerIssuer, "alice");
        var oneWay = Trip.Create("One-way", original.TimeZone, original.Start, original.End, false,
            [original.Legs[0]], original.TripId);
        var roundTrip = Trip.Create("Round trip", original.TimeZone, original.Start, original.End, true,
            original.Legs, original.TripId);

        // each writer pauses at its first UPDATE until the other one arrives, so both really overlap in the database.
        var rendezvous = new WriteRendezvous(2);
        await using var first = CreateContext(database.ConnectionString, new RendezvousCommandInterceptor(rendezvous));
        await using var second = CreateContext(database.ConnectionString, new RendezvousCommandInterceptor(rendezvous));
        var writes = await Task.WhenAll(
            Task.Run(() => new EfTripRepository(first).ReplaceOwnedAsync(oneWay, OwnerIssuer, "alice", original.Revision)),
            Task.Run(() => new EfTripRepository(second).ReplaceOwnedAsync(roundTrip, OwnerIssuer, "alice", original.Revision)));
        Assert.True(rendezvous.Completed, "both writers must have reached the UPDATE together.");

        Assert.Single(writes, status => status == TripReplacementStatus.Replaced);
        Assert.Single(writes, status => status == TripReplacementStatus.Stale);
        var persisted = await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(persisted);
        var expected = writes[0] == TripReplacementStatus.Replaced ? oneWay : roundTrip;
        Assert.Equal(original.Revision + 1, persisted.Revision);
        Assert.Equal(expected.Name, persisted.Name);
        Assert.Equal(expected.Legs, persisted.Legs);
        Assert.Equal(expected.Stops, persisted.Stops);
    }

    [Fact]
    public async Task Read_ReplacementBetweenParentAndChildren_ReturnsOneCompleteRevision()
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        var replacement = Trip.Create("Changed while reading", Paris, Start, Start, false, [], original.TripId,
            stops: [original.Stops[1]]);
        await using (var setup = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(setup);
            await new EfTripRepository(setup).AddAsync(original, OwnerIssuer, "alice");
        }

        var concurrentWrite = new ReplaceBeforeLegReadInterceptor(async cancellationToken =>
        {
            await using var writer = CreateContext(database.ConnectionString);
            Assert.Equal(TripReplacementStatus.Replaced,
                await new EfTripRepository(writer).ReplaceOwnedAsync(replacement, OwnerIssuer, "alice", 1, cancellationToken));
        });
        await using var reader = new TripsDbContext(new DbContextOptionsBuilder<TripsDbContext>()
            .UseNpgsql(database.ConnectionString).AddInterceptors(concurrentWrite).Options);
        var read = await new EfTripRepository(reader).GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");

        Assert.True(concurrentWrite.ReplacementCommitted);
        Assert.NotNull(read);
        Assert.Equal(original.Revision, read.Revision);
        Assert.Equal(original.Name, read.Name);
        Assert.Equal(original.Stops, read.Stops);
        Assert.Equal(original.Legs, read.Legs);

        await using var fresh = CreateContext(database.ConnectionString);
        var current = await new EfTripRepository(fresh).GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(current);
        Assert.Equal(2, current.Revision);
        Assert.Equal(replacement.Name, current.Name);
        Assert.Equal(replacement.Stops, current.Stops);
        Assert.Empty(current.Legs);
    }

    [Fact]
    public async Task OwnerScoping_HidesTripFromOtherSubjectsAndOtherIssuers()
    {
        await using var database = new PostgresTestDatabase();
        var trip = SampleTrip();
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            await new EfTripRepository(context).AddAsync(trip, OwnerIssuer, "alice");
        }

        await using var query = CreateContext(database.ConnectionString);
        var repository = new EfTripRepository(query);
        Assert.Null(await repository.GetOwnedByIdAsync(trip.TripId, OwnerIssuer, "bob"));
        Assert.Null(await repository.GetOwnedByIdAsync(trip.TripId, "https://other-issuer.example/", "alice"));
        Assert.NotNull(await repository.GetOwnedByIdAsync(trip.TripId, OwnerIssuer, "alice"));
    }

    [Fact]
    public async Task PostgreSqlConstraints_RejectBlankOwnersAndEndBeforeStart()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await TripsDatabase.InitializeAsync(context);

        var blankOwner = await Assert.ThrowsAsync<PostgresException>(() => InsertTripAsync(context, ownerSubject: " ", end: Start));
        Assert.Equal(PostgresErrorCodes.CheckViolation, blankOwner.SqlState);
        Assert.Equal("CK_Trips_Owner", blankOwner.ConstraintName);

        var endBeforeStart = await Assert.ThrowsAsync<PostgresException>(() => InsertTripAsync(context, ownerSubject: "alice", end: Start.AddHours(-1)));
        Assert.Equal(PostgresErrorCodes.CheckViolation, endBeforeStart.SqlState);
        Assert.Equal("CK_Trips_End", endBeforeStart.ConstraintName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task EmptyOrSingleStop_SurvivesRestart_WithoutInventingLegs(bool singleStop, bool roundTrip)
    {
        await using var database = new PostgresTestDatabase();
        var expected = Trip.Create("Early plan", Paris, Start, Start, roundTrip, [],
            stops: singleStop ? [SampleTrip().Legs[0].To] : []);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            await new EfTripRepository(context).AddAsync(expected, OwnerIssuer, "alice");
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var repository = new EfTripRepository(restarted);
        var actual = await repository.GetOwnedByIdAsync(expected.TripId, OwnerIssuer, "alice");
        Assert.NotNull(actual);
        Assert.Equal(expected.Stops, actual.Stops);
        Assert.Empty(actual.Legs);
        Assert.Equal(roundTrip, actual.IsRoundTrip);
        Assert.Equal(Start, actual.Start);
        Assert.Equal(Start.Offset, actual.Start.Offset);
        Assert.Null(await repository.GetOwnedByIdAsync(expected.TripId, OwnerIssuer, "bob"));
        Assert.Null(await repository.GetOwnedByIdAsync(expected.TripId, "https://other-issuer.example/", "alice"));
        Assert.Equal(expected.TripId, Assert.Single(await repository.ListOwnedAsync(OwnerIssuer, "alice")).TripId);
        Assert.Equal(singleStop ? 1L : 0L,
            await restarted.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());
        Assert.Equal(0L, await restarted.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripLegs\"").SingleAsync());
    }

    [Theory]
    [InlineData("attraction", "accommodation", false)]
    [InlineData("transport_stop", "transport_stop", true)]
    [InlineData("accommodation", "accommodation", true)]
    public async Task UnrestrictedComposition_SavesSnapshots_WithoutForcedReturnOrAttraction(string fromType, string toType, bool roundTrip)
    {
        await using var database = new PostgresTestDatabase();
        var sample = SampleTrip();
        var leg = sample.Legs[0] with { From = sample.Legs[0].From with { Type = fromType }, To = sample.Legs[0].To with { Type = toType } };
        var expected = Trip.Create("Chosen places", Paris, Start, Start.AddHours(4), roundTrip, [leg]);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            await new EfTripRepository(context).AddAsync(expected, OwnerIssuer, "alice");
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var actual = await new EfTripRepository(restarted).GetOwnedByIdAsync(expected.TripId, OwnerIssuer, "alice");
        Assert.NotNull(actual);
        Assert.Equal(expected.Stops, actual.Stops);
        Assert.Equal(expected.Legs, actual.Legs);
        Assert.Equal(roundTrip, actual.IsRoundTrip);
    }

    [Fact]
    public async Task ReplaceOwned_TransitionsBetweenLegsSingleStopAndEmpty_WithoutStaleChildren()
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        await using var context = CreateContext(database.ConnectionString);
        await TripsDatabase.InitializeAsync(context);
        var repository = new EfTripRepository(context);
        await repository.AddAsync(original, OwnerIssuer, "alice");
        var single = Trip.Create("One place", Paris, Start, Start, true, [], original.TripId, stops: [original.Stops[1]]);
        Assert.Equal(TripReplacementStatus.NotFound, await repository.ReplaceOwnedAsync(single, OwnerIssuer, "bob", 1));
        Assert.Equal(TripReplacementStatus.NotFound, await repository.ReplaceOwnedAsync(single, "https://other-issuer.example/", "alice", 1));
        Assert.Equal(TripReplacementStatus.Replaced, await repository.ReplaceOwnedAsync(single, OwnerIssuer, "alice", 1));
        Assert.Equal(single.Stops, (await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice"))!.Stops);
        Assert.Equal(0L, await context.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripLegs\"").SingleAsync());
        Assert.Equal(1L, await context.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());

        var empty = Trip.Create("No places yet", Paris, Start, Start, false, [], original.TripId);
        Assert.Equal(TripReplacementStatus.Stale, await repository.ReplaceOwnedAsync(empty, OwnerIssuer, "alice", 1));
        Assert.Equal(single.Stops, (await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice"))!.Stops);
        Assert.Equal(TripReplacementStatus.Replaced, await repository.ReplaceOwnedAsync(empty, OwnerIssuer, "alice", 2));
        var savedEmpty = await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(savedEmpty);
        Assert.Equal(3, savedEmpty.Revision);
        Assert.Empty(savedEmpty.Stops);
        Assert.Empty(savedEmpty.Legs);
        Assert.Equal(0L, await context.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());

        Assert.Equal(TripReplacementStatus.Replaced, await repository.ReplaceOwnedAsync(original, OwnerIssuer, "alice", 3));
        var restored = await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(restored);
        Assert.Equal(4, restored.Revision);
        Assert.Equal(original.Stops, restored.Stops);
        Assert.Equal(original.Legs, restored.Legs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedStopWrite_RollsBackTripAndAllChildren(bool replacing)
    {
        await using var database = new PostgresTestDatabase();
        var original = SampleTrip();
        var candidate = Trip.Create("One place", Paris, Start, Start, false, [], original.TripId, stops: [original.Stops[1]]);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await TripsDatabase.InitializeAsync(context);
            var repository = new EfTripRepository(context);
            if (replacing) await repository.AddAsync(original, OwnerIssuer, "alice");
            await context.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION reject_test_stop() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'test stop failure' USING ERRCODE = '23514'; END; $$;
                CREATE TRIGGER reject_test_stop BEFORE INSERT ON "TripStops"
                FOR EACH ROW EXECUTE FUNCTION reject_test_stop();
                """);
            var exception = await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                if (replacing) await repository.ReplaceOwnedAsync(candidate, OwnerIssuer, "alice", 1);
                else await repository.AddAsync(candidate, OwnerIssuer, "alice");
            });
            Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(exception.InnerException).SqlState);
        }

        await using var check = CreateContext(database.ConnectionString);
        var actual = await new EfTripRepository(check).GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        if (replacing)
        {
            Assert.NotNull(actual);
            Assert.Equal(original.Name, actual.Name);
            Assert.Equal(original.Revision, actual.Revision);
            Assert.Equal(original.Stops, actual.Stops);
            Assert.Equal(original.Legs, actual.Legs);
        }
        else Assert.Null(actual);
        Assert.Equal(replacing ? 1L : 0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"Trips\"").SingleAsync());
        Assert.Equal(replacing ? 2L : 0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripLegs\"").SingleAsync());
        Assert.Equal(replacing ? 3L : 0L, await check.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());
    }

    [Theory]
    [InlineData("20261109022113_TripRevision")]
    [InlineData("20261109120000_TripRevisionCheck")]
    public async Task StopMigration_PreservesLegacyTripAndDerivesStopsFromItsLegs(string previousMigration)
    {
        await using var database = new PostgresTestDatabase();
        var sample = SampleTrip();
        var original = Trip.Create(sample.Name, Paris, sample.Start.ToUniversalTime(), sample.End.ToUniversalTime(),
            sample.IsRoundTrip, sample.Legs, sample.TripId);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await context.GetService<IMigrator>().MigrateAsync(previousMigration);
            await InsertLegacyTripAsync(context, original);
            await TripsDatabase.InitializeAsync(context);
            Assert.False(context.Database.HasPendingModelChanges());
            var invalidRevision = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"Trips\" SET \"Revision\" = {0L} WHERE \"TripId\" = {original.TripId}"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, invalidRevision.SqlState);
            Assert.Equal("CK_Trips_Revision", invalidRevision.ConstraintName);
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var repository = new EfTripRepository(restarted);
        var actual = await repository.GetOwnedByIdAsync(original.TripId, OwnerIssuer, "alice");
        Assert.NotNull(actual);
        Assert.Equal(original.Name, actual.Name);
        Assert.Equal(original.Start, actual.Start);
        Assert.Equal(original.End, actual.End);
        Assert.Equal(original.TripId, actual.TripId);
        Assert.Equal(original.Revision, actual.Revision);
        Assert.Equal(TimeSpan.Zero, actual.Start.Offset);
        Assert.Equal(TimeSpan.Zero, actual.End.Offset);
        Assert.Equal(TimeSpan.FromHours(2), actual.Legs[0].Departure.Offset);
        Assert.Equal(original.Stops, actual.Stops);
        Assert.Equal(original.Legs, actual.Legs);
        Assert.Equal(0L, await restarted.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());
        Assert.Equal(TripReplacementStatus.Replaced, await repository.ReplaceOwnedAsync(actual, OwnerIssuer, "alice", 1));
        Assert.Equal(3L, await restarted.Database.SqlQuery<long>($"SELECT COUNT(*) AS \"Value\" FROM \"TripStops\"").SingleAsync());
    }

    [Theory]
    [InlineData(91, 0, "attraction", 1, "CK_TripStops_Latitude")]
    [InlineData(0, -181, "transport_stop", 1, "CK_TripStops_Longitude")]
    [InlineData(0, 0, "unknown", 1, "CK_TripStops_Type")]
    [InlineData(0, 0, "accommodation", 0, "CK_TripStops_Sequence")]
    public async Task StopConstraints_RejectInvalidSnapshots(decimal latitude, decimal longitude, string type, int sequence, string constraint)
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await TripsDatabase.InitializeAsync(context);
        var trip = Trip.Create("Empty", Paris, Start, Start, false, []);
        await new EfTripRepository(context).AddAsync(trip, OwnerIssuer, "alice");
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TripStops" ("TripId", "Sequence", "PlaceId", "PlaceType", "PlaceName", "PlaceAddress", "Latitude", "Longitude")
            VALUES ({trip.TripId}, {sequence}, {Guid.NewGuid()}, {type}, {"Chosen place"}, {""}, {latitude}, {longitude})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraint, exception.ConstraintName);
    }

    /**
     * Seeds the old schema directly so migration preservation is tested without the new stop mapping.
     * @param context Context migrated before the stop snapshot table.
     * @param trip Existing complete itinerary to copy.
     * @return Completion once its old-schema rows have been written.
     */
    private static async Task InsertLegacyTripAsync(TripsDbContext context, Trip trip)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Trips" ("TripId", "OwnerIssuer", "OwnerSubject", "Name", "TimeZoneId", "StartAt", "StartOffset", "EndAt", "EndOffset", "IsRoundTrip")
            VALUES ({trip.TripId}, {OwnerIssuer}, {"alice"}, {trip.Name}, {trip.TimeZone.Id}, {trip.Start.ToUniversalTime()}, {trip.Start.Offset}, {trip.End.ToUniversalTime()}, {trip.End.Offset}, {trip.IsRoundTrip})
            """);
        foreach (var leg in trip.Legs)
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "TripLegs" ("TripId", "Sequence", "FromPlaceId", "FromPlaceType", "FromPlaceName", "FromPlaceAddress", "FromLatitude", "FromLongitude", "ToPlaceId", "ToPlaceType", "ToPlaceName", "ToPlaceAddress", "ToLatitude", "ToLongitude", "TransportModeId", "TransportCode", "TransportName", "DepartureAt", "DepartureOffset", "ArrivalAt", "ArrivalOffset")
                VALUES ({trip.TripId}, {leg.Sequence}, {leg.From.PlaceId}, {leg.From.Type}, {leg.From.Name}, {leg.From.Address}, {leg.From.Latitude}, {leg.From.Longitude}, {leg.To.PlaceId}, {leg.To.Type}, {leg.To.Name}, {leg.To.Address}, {leg.To.Latitude}, {leg.To.Longitude}, {leg.Transport.TransportModeId}, {leg.Transport.Code}, {leg.Transport.Name}, {leg.Departure.ToUniversalTime()}, {leg.Departure.Offset}, {leg.Arrival.ToUniversalTime()}, {leg.Arrival.Offset})
                """);
    }

    /**
     * Inserts a trip row directly so the database's own check constraints are exercised.
     *
     * @param context Migrated test database context.
     * @param ownerSubject Owner subject to write.
     * @param end End instant to write.
     * @return Completion after the insert runs.
     */
    private static Task<int> InsertTripAsync(TripsDbContext context, string ownerSubject, DateTimeOffset end) =>
        context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Trips" ("TripId", "OwnerIssuer", "OwnerSubject", "Name", "TimeZoneId", "StartAt", "StartOffset", "EndAt", "EndOffset", "IsRoundTrip")
            VALUES ({Guid.CreateVersion7()}, {OwnerIssuer}, {ownerSubject}, {"t"}, {"Europe/Paris"}, {Start.ToUniversalTime()}, {TimeSpan.FromHours(2)}, {end.ToUniversalTime()}, {TimeSpan.FromHours(2)}, {false})
            """);

    /**
     * Builds a two-leg round trip from a stored accommodation to an attraction and back, copying the place values
     * the way the save path does.
     *
     * @param secondTransportCode Optional code for the second leg; longer than the column allows when testing rollback.
     * @return A valid trip with a new identifier.
     */
    private static Trip SampleTrip(string secondTransportCode = "bus")
    {
        var hotel = new TripPlace(Guid.Parse("0196a1b2-0000-7000-8000-000000000001"), "accommodation", "Hotel Lumiere", "1 rue de Rivoli", 48.85m, 2.35m);
        var museum = new TripPlace(Guid.Parse("0196a1b2-0000-7000-8000-000000000002"), "attraction", "Louvre", "Rue de Rivoli", 48.86m, 2.34m);
        var train = new TripTransport(Guid.Parse("0196a1b2-0000-7000-8000-000000000004"), "train", "Train");
        var bus = new TripTransport(Guid.Parse("0196a1b2-0000-7000-8000-000000000005"), secondTransportCode, "Bus");
        return Trip.Create("Paris weekend", Paris, Start, Start.AddHours(4), true,
        [
            new(1, hotel, museum, train, Start.AddHours(1), Start.AddHours(2)),
            new(2, museum, hotel, bus, Start.AddHours(3), Start.AddHours(4))
        ]);
    }

    /**
     * Creates a PostgreSQL context isolated to the disposable test database.
     *
     * @param connectionString Connection string for this test's database.
     * @param interceptors Optional EF interceptors for the context.
     * @return A new context that can be disposed independently.
     */
    private static TripsDbContext CreateContext(string connectionString, params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options);
}
