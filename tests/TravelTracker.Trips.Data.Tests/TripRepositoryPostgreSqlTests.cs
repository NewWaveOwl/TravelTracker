using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Data;
using TravelTracker.Trips.Data.Repositories;

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
        var persisted = await new EfTripRepository(restarted).GetOwnedByIdAsync(expected.TripId, OwnerIssuer, "alice");
        var migrations = await restarted.Database.GetAppliedMigrationsAsync();

        Assert.NotNull(persisted);
        Assert.Equal(expected.TripId, persisted.TripId);
        Assert.Equal("Paris weekend", persisted.Name);
        Assert.Equal("Europe/Paris", persisted.TimeZone.Id);
        Assert.Equal(expected.Start, persisted.Start);
        Assert.Equal(TimeSpan.FromHours(2), persisted.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(2), persisted.End.Offset);
        Assert.True(persisted.IsRoundTrip);
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
        Assert.Contains("20261106194443_InitialTrips", migrations);
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
     * @return A new context that can be disposed independently.
     */
    private static TripsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<TripsDbContext>().UseNpgsql(connectionString).Options);

    /**
     * Creates and later drops one random PostgreSQL database for a relational test.
     */
    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName = "traveltracker_trips_test_" + Guid.NewGuid().ToString("N");

        public string ConnectionString { get; }

        public PostgresTestDatabase()
        {
            adminConnectionString = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
                throw new InvalidOperationException("PostgreSQL data tests require TRAVELTRACKER_TEST_POSTGRES with CREATEDB permission.");
            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            create.ExecuteNonQuery();
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString)
            {
                Database = databaseName,
                Pooling = false
            }.ConnectionString;
        }

        public ValueTask DisposeAsync()
        {
            using var admin = new NpgsqlConnection(adminConnectionString);
            admin.Open();
            using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
            drop.ExecuteNonQuery();
            return ValueTask.CompletedTask;
        }
    }
}
