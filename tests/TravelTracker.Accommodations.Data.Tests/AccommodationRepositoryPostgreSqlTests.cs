using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Accommodations.Data;
using TravelTracker.Accommodations.Data.Repositories;
using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Data.Tests;

/**
 * Verifies PostgreSQL migration, persistence, and visibility query behavior.
 */
[Trait("Category", "PostgreSQL")]
public sealed class AccommodationRepositoryPostgreSqlTests
{
    [Fact]
    public async Task MigrationAndRepository_PreservePlaceAcrossContextRestart()
    {
        await using var database = new PostgresTestDatabase();
        var expected = Accommodation("Restarted Place", OwnerIssuer, "alice", isShared: false);
        {
            await using var context = CreateContext(database.ConnectionString);
            await AccommodationsDatabase.InitializeAsync(context);
            await new EfAccommodationRepository(context).AddAsync(expected);
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var repository = new EfAccommodationRepository(restarted);
        var persisted = await repository.GetVisibleByIdAsync(expected.AccommodationId, OwnerIssuer, "alice", false);
        var migrations = await restarted.Database.GetAppliedMigrationsAsync();

        Assert.Equal("Restarted Place", persisted?.Name);
        Assert.Equal("Test address", persisted?.Address);
        Assert.Equal(45.25m, persisted?.Latitude);
        Assert.Equal(-73.75m, persisted?.Longitude);
        Assert.Equal(OwnerIssuer, persisted?.OwnerIssuer);
        Assert.Equal("alice", persisted?.OwnerSubject);
        Assert.False(persisted!.IsShared);
        Assert.Contains("20261105185817_InitialAccommodations", migrations);
    }

    [Fact]
    public async Task VisibilityQueries_OnlyReturnSharedAndOwnPrivateRecords()
    {
        await using var database = new PostgresTestDatabase();
        await using (var context = CreateContext(database.ConnectionString))
        {
            await AccommodationsDatabase.InitializeAsync(context);
            var repository = new EfAccommodationRepository(context);
            await repository.AddAsync(Accommodation("Alice private", OwnerIssuer, "alice", false));
            await repository.AddAsync(Accommodation("Bob private", OwnerIssuer, "bob", false));
            await repository.AddAsync(Accommodation("Shared catalogue", OwnerIssuer, "admin", true));
        }

        await using var queryContext = CreateContext(database.ConnectionString);
        var queryRepository = new EfAccommodationRepository(queryContext);
        var aliceVisible = await queryRepository.GetVisibleAsync(OwnerIssuer, "alice", includeShared: true);
        var bobVisible = await queryRepository.GetVisibleAsync(OwnerIssuer, "bob", includeShared: true);
        var bobPrivateOnly = await queryRepository.GetVisibleAsync(OwnerIssuer, "bob", includeShared: false);

        Assert.Equal(new[] { "Alice private", "Shared catalogue" },
            aliceVisible.Select(record => record.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Bob private", "Shared catalogue" },
            bobVisible.Select(record => record.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Bob private" }, bobPrivateOnly.Select(record => record.Name));
    }

    [Fact]
    public async Task DetailQuery_HidesAnotherOwnersPrivateRecordAndSharedWhenCallerCannotReadShared()
    {
        await using var database = new PostgresTestDatabase();
        var privateRecord = Accommodation("Alice private", OwnerIssuer, "alice", false);
        var sharedRecord = Accommodation("Shared catalogue", OwnerIssuer, "admin", true);
        await using (var context = CreateContext(database.ConnectionString))
        {
            await AccommodationsDatabase.InitializeAsync(context);
            var repository = new EfAccommodationRepository(context);
            await repository.AddAsync(privateRecord);
            await repository.AddAsync(sharedRecord);
        }

        await using var queryContext = CreateContext(database.ConnectionString);
        var repositoryForBob = new EfAccommodationRepository(queryContext);

        Assert.Null(await repositoryForBob.GetVisibleByIdAsync(privateRecord.AccommodationId, OwnerIssuer, "bob", true));
        Assert.Null(await repositoryForBob.GetVisibleByIdAsync(sharedRecord.AccommodationId, OwnerIssuer, "bob", false));
        Assert.Equal(sharedRecord.AccommodationId,
            (await repositoryForBob.GetVisibleByIdAsync(sharedRecord.AccommodationId, OwnerIssuer, "bob", true))?.AccommodationId);
    }

    [Fact]
    public async Task PostgreSqlConstraints_RejectBlankNamesInvalidCoordinatesAndMissingOwner()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await AccommodationsDatabase.InitializeAsync(context);

        await AssertConstraintViolationAsync(context, "CK_Accommodations_Name", name: " ");
        await AssertConstraintViolationAsync(context, "CK_Accommodations_Coordinates", latitude: 90.1m);
        await AssertConstraintViolationAsync(context, "CK_Accommodations_Owner", ownerSubject: " ");
    }

    private const string OwnerIssuer = "https://issuer.example/";

    /**
     * Creates a PostgreSQL context isolated to the disposable test database.
     * @param connectionString Connection string for this test's database.
     * @return A new context that can be disposed independently.
     */
    private static AccommodationsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AccommodationsDbContext>().UseNpgsql(connectionString).Options);

    /**
     * Creates a valid record with controlled ownership and visibility.
     * @param name Accommodation name.
     * @param issuer Validated owner issuer.
     * @param subject Validated owner subject.
     * @param isShared Whether the place belongs to the shared catalogue.
     * @return A persistable accommodation.
     */
    private static Accommodation Accommodation(string name, string issuer, string subject, bool isShared) => new()
    {
        AccommodationId = Guid.CreateVersion7(),
        Name = name,
        Address = "Test address",
        Latitude = 45.25m,
        Longitude = -73.75m,
        OwnerIssuer = issuer,
        OwnerSubject = subject,
        IsShared = isShared
    };

    /**
     * Inserts an invalid row and verifies the named PostgreSQL check constraint rejects it.
     * @param context Migrated test database context.
     * @param constraintName Constraint expected to reject the row.
     * @param name Optional name override.
     * @param latitude Optional latitude override.
     * @param ownerSubject Optional owner subject override.
     * @return Completion after the database rejects the invalid row.
     */
    private static async Task AssertConstraintViolationAsync(AccommodationsDbContext context, string constraintName,
        string name = "Valid place", decimal latitude = 45m, string ownerSubject = "owner")
    {
        var id = Guid.CreateVersion7();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "Accommodations"
                ("AccommodationId", "Name", "Address", "Latitude", "Longitude", "OwnerIssuer", "OwnerSubject", "IsShared")
            VALUES ({id}, {name}, {"Test address"}, {latitude}, {-73m}, {OwnerIssuer}, {ownerSubject}, {false})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    /**
     * Creates and later drops one random PostgreSQL database for a relational test.
     */
    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName = "traveltracker_accommodations_test_" + Guid.NewGuid().ToString("N");

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
