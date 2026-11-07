using Microsoft.EntityFrameworkCore;
using Npgsql;
using TravelTracker.Settings.Data;
using TravelTracker.Settings.Data.Repositories;
using TravelTracker.Settings.Domain.Models;

namespace TravelTracker.Settings.Data.Tests;

/**
 * verifies PostgreSQL migration, per-account persistence, restart durability and database-enforced preference checks.
 */
[Trait("Category", "PostgreSQL")]
public sealed class SettingsRepositoryPostgreSqlTests
{
    private const string Issuer = "https://issuer.example.test";

    [Fact]
    public async Task Migration_PersistsSavedPreferencesAcrossContextRestart()
    {
        await using var database = new PostgresTestDatabase();
        var saved = Preferences(theme: "dark", latitude: 45.25m, longitude: -73.75m, zoom: 12, mode: "train", timeZone: "America/Montreal");
        await using (var context = CreateContext(database.ConnectionString))
        {
            await SettingsDatabase.InitializeAsync(context);
            await new EfSettingsRepository(context).ReplaceIfRevisionAsync(Issuer, "alice", saved, expectedRevision: 0);
        }

        await using var restarted = CreateContext(database.ConnectionString);
        var persisted = await new EfSettingsRepository(restarted).GetByOwnerAsync(Issuer, "alice");
        var migrations = await restarted.Database.GetAppliedMigrationsAsync();

        Assert.NotNull(persisted);
        Assert.Equal(1, persisted.Revision);
        Assert.Equal("dark", persisted.Theme);
        Assert.Equal(45.25m, persisted.DefaultMapLatitude);
        Assert.Equal(-73.75m, persisted.DefaultMapLongitude);
        Assert.Equal(12, persisted.DefaultMapZoom);
        Assert.Equal("train", persisted.PreferredTransportMode);
        Assert.Equal("America/Montreal", persisted.TimeZone);
        Assert.Contains(migrations, migration => migration.EndsWith("_InitialSettings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetByOwner_ReturnsNullBeforeFirstSave()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);

        var persisted = await new EfSettingsRepository(context).GetByOwnerAsync(Issuer, "nobody");

        Assert.Null(persisted);
    }

    [Fact]
    public async Task Replace_UpdatesOnlyTheOwnersRowAndAdvancesItsRevision()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);
        var repository = new EfSettingsRepository(context);
        await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "light"), expectedRevision: 0);
        await repository.ReplaceIfRevisionAsync(Issuer, "bob", Preferences(theme: "dark"), expectedRevision: 0);

        var replaced = await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "system", mode: "bike"), expectedRevision: 1);

        var alice = await repository.GetByOwnerAsync(Issuer, "alice");
        var bob = await repository.GetByOwnerAsync(Issuer, "bob");
        Assert.Equal(2, replaced?.Revision);
        Assert.Equal(2, alice?.Revision);
        Assert.Equal("system", alice?.Theme);
        Assert.Equal("bike", alice?.PreferredTransportMode);
        Assert.Equal(1, bob?.Revision);
        Assert.Equal("dark", bob?.Theme);
    }

    [Fact]
    public async Task Replace_RejectsStaleRevisionAndKeepsStoredValues()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);
        var repository = new EfSettingsRepository(context);
        await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "light"), expectedRevision: 0);
        await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "dark"), expectedRevision: 1);

        var stale = await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "neon"), expectedRevision: 1);

        var persisted = await repository.GetByOwnerAsync(Issuer, "alice");
        Assert.Null(stale);
        Assert.Equal("dark", persisted?.Theme);
        Assert.Equal(2, persisted?.Revision);
    }

    [Fact]
    public async Task Replace_FirstSaveWithExpectedZeroRejectsWhenRowAlreadyExists()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);
        var repository = new EfSettingsRepository(context);
        var first = await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "light"), expectedRevision: 0);

        var second = await repository.ReplaceIfRevisionAsync(Issuer, "alice", Preferences(theme: "dark"), expectedRevision: 0);

        Assert.Equal(1, first?.Revision);
        Assert.Null(second);
        Assert.Equal("light", (await repository.GetByOwnerAsync(Issuer, "alice"))?.Theme);
    }

    [Fact]
    public async Task Database_RejectsUnsupportedThemeWithNamedConstraint()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);

        await AssertConstraintViolationAsync(context, "CK_UserSettings_Theme", theme: "neon");
    }

    [Fact]
    public async Task Database_RejectsOutOfRangeCoordinatesWithNamedConstraint()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);

        await AssertConstraintViolationAsync(context, "CK_UserSettings_Coordinates", latitude: 91m);
    }

    [Fact]
    public async Task Database_RejectsUnsupportedTransportModeWithNamedConstraint()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);

        await AssertConstraintViolationAsync(context, "CK_UserSettings_TransportMode", mode: "boat");
    }

    [Fact]
    public async Task Database_RejectsBlankOwnerWithNamedConstraint()
    {
        await using var database = new PostgresTestDatabase();
        await using var context = CreateContext(database.ConnectionString);
        await SettingsDatabase.InitializeAsync(context);

        await AssertConstraintViolationAsync(context, "CK_UserSettings_Owner", subject: "   ");
    }

    /**
     * builds a valid preference set, changing only the supplied values.
     * @param theme the theme value.
     * @param latitude the default map latitude.
     * @param longitude the default map longitude.
     * @param zoom the default map zoom.
     * @param mode the preferred transport code.
     * @param timeZone the IANA time zone identifier.
     * @return a populated domain value object.
     */
    private static UserSettings Preferences(string theme = "system", decimal latitude = 0m, decimal longitude = 0m,
        int zoom = 2, string mode = "car", string timeZone = "UTC") => new()
    {
        Theme = theme,
        DefaultMapLatitude = latitude,
        DefaultMapLongitude = longitude,
        DefaultMapZoom = zoom,
        PreferredTransportMode = mode,
        TimeZone = timeZone
    };

    /**
     * creates a fresh context for one PostgreSQL database.
     * @param connectionString the isolated database connection.
     * @return a context that owns its own connection pool.
     */
    private static SettingsDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<SettingsDbContext>().UseNpgsql(connectionString).Options);

    /**
     * inserts one invalid row and verifies the named check constraint rejects it.
     * @param context a migrated test database context.
     * @param constraintName the constraint expected to reject the row.
     * @param theme the theme value to write.
     * @param latitude the latitude value to write.
     * @param mode the transport code to write.
     * @param subject the owner subject to write.
     * @return completion after the database rejects the row.
     */
    private static async Task AssertConstraintViolationAsync(SettingsDbContext context, string constraintName,
        string theme = "light", decimal latitude = 45m, string mode = "car", string subject = "owner")
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "UserSettings"
                ("OwnerIssuer", "OwnerSubject", "Theme", "DefaultMapLatitude", "DefaultMapLongitude", "DefaultMapZoom", "PreferredTransportMode", "TimeZone", "Revision")
            VALUES ({Issuer}, {subject}, {theme}, {latitude}, {-73m}, {2}, {mode}, {"UTC"}, {1L})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal(constraintName, exception.ConstraintName);
    }

    /**
     * creates and later drops one random PostgreSQL database for a relational test.
     */
    private sealed class PostgresTestDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName = "traveltracker_settings_test_" + Guid.NewGuid().ToString("N");

        public string ConnectionString { get; }

        /**
         * creates the isolated database through the explicitly configured test role.
         */
        public PostgresTestDatabase()
        {
            adminConnectionString = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
                throw new InvalidOperationException("PostgreSQL settings tests require TRAVELTRACKER_TEST_POSTGRES with CREATEDB permission.");
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

        /**
         * drops only this randomly named database.
         * @return a completed task after the database is dropped.
         */
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
