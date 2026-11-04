using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories;

namespace TravelTracker.Users.Tests;

/** PostgreSQL-only evidence for durable first-admin consumption, concurrent initialization and rollback. */
[Trait("Category", "PostgreSQL")]
public sealed class PersistenceTests
{
    const string Issuer = "https://identity.example.test/";
    const string Email = "administrator@example.test";

    [Fact]
    public async Task MigrationAndRestartPreserveTheFirstAdministratorAndAudit()
    {
        await using var database = await Database.CreateAsync();
        var instant = DateTimeOffset.Parse("2026-11-04T12:34:56.1234567Z");
        var first = await database.Repository(new FixedClock(instant)).BootstrapAsync(Issuer, "account-one", Email, default);
        Assert.NotNull(first);
        Assert.Equal(0, first.CreatedAt.Ticks % 10);
        Assert.Equal(instant.Ticks - 7, first.CreatedAt.Ticks);
        var restarted = database.Repository();
        var read = await restarted.FindAsync(Issuer, "account-one", default);
        Assert.Equal(first, read);
        Assert.Equal("administrator", read!.Role);
        Assert.Equal(first, await restarted.BootstrapAsync(Issuer, "account-one", Email, default));
        Assert.Null(await restarted.BootstrapAsync(Issuer, "another-account", Email, default));
        await using var context = database.Context();
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(1, await Count(context, "UserAccounts"));
        Assert.Equal(1, await Count(context, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(context, "AdministratorAudit"));
    }

    [Fact]
    public async Task ConcurrentHostsPermitOnlyOneBootstrapIdentity()
    {
        await using var database = await Database.CreateAsync();
        var attempts = await Task.WhenAll(
            database.Repository().BootstrapAsync(Issuer, "account-one", Email, default),
            database.Repository().BootstrapAsync(Issuer, "account-two", Email, default));
        Assert.Single(attempts, account => account is not null);
        await using var context = database.Context();
        Assert.Equal(1, await Count(context, "UserAccounts"));
        Assert.Equal(1, await Count(context, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(context, "AdministratorAudit"));
    }

    [Fact]
    public async Task DemotingTheOriginalAccountNeverReopensOrReElevatesBootstrap()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        await using (var context = database.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserAccounts\" SET \"Role\" = 'traveller' WHERE \"UserId\" = {first!.UserId}");
        var replay = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        Assert.Equal("traveller", replay!.Role);
        Assert.Equal(first!.UserId, replay.UserId);
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-two", Email, default));
        await using var check = database.Context();
        Assert.Equal(1, await Count(check, "AdministratorAudit"));
    }

    [Fact]
    public async Task RemovingTheOriginalAccountDoesNotReopenBootstrap()
    {
        await using var database = await Database.CreateAsync();
        var repository = database.Repository();
        var first = await repository.BootstrapAsync(Issuer, "account-one", Email, default);
        await using (var context = database.Context())
            await context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"UserAccounts\" WHERE \"UserId\" = {first!.UserId}");
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-one", Email, default));
        Assert.Null(await repository.BootstrapAsync(Issuer, "account-two", Email, default));
        await using var check = database.Context();
        Assert.Equal(0, await Count(check, "UserAccounts"));
        Assert.Equal(1, await Count(check, "AdministratorBootstrap"));
        Assert.Equal(1, await Count(check, "AdministratorAudit"));
    }

    [Fact]
    public async Task FailedAuditWriteRollsBackTheAccountAndBootstrapMarkerTogether()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Context())
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" ADD CONSTRAINT reject_test_audit CHECK (false)");
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Repository().BootstrapAsync(Issuer, "account-one", Email, default));
        await using var check = database.Context();
        Assert.Equal(0, await Count(check, "UserAccounts"));
        Assert.Equal(0, await Count(check, "AdministratorBootstrap"));
        Assert.Equal(0, await Count(check, "AdministratorAudit"));
        await check.Database.ExecuteSqlRawAsync("ALTER TABLE \"AdministratorAudit\" DROP CONSTRAINT reject_test_audit");
        Assert.NotNull(await database.Repository().BootstrapAsync(Issuer, "account-one", Email, default));
    }

    static Task<int> Count(UsersDbContext context, string table) => (table switch
    {
        "UserAccounts" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"UserAccounts\""),
        "AdministratorBootstrap" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"AdministratorBootstrap\""),
        "AdministratorAudit" => context.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM \"AdministratorAudit\""),
        _ => throw new ArgumentException("Unknown test table.", nameof(table)),
    }).SingleAsync();

    sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }

    /** Owns one uniquely named disposable test database, never a deployment database. */
    sealed class Database(string administrativeConnection, string name, string connection) : IAsyncDisposable
    {
        public UsersDbContext Context() => new(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options);
        public EfUserRepository Repository(TimeProvider? clock = null) => new(new PooledDbContextFactory<UsersDbContext>(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options), clock);

        /**
         * Creates and migrates an isolated PostgreSQL database using an explicitly supplied test connection.
         * @return The disposable test database.
         */
        public static async Task<Database> CreateAsync()
        {
            var administrativeConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ?? throw new InvalidOperationException("Set TRAVELTRACKER_TEST_POSTGRES to an isolated PostgreSQL instance with CREATEDB permission.");
            var name = "traveltracker_users_test_" + Guid.NewGuid().ToString("N");
            await using var administrative = new NpgsqlConnection(administrativeConnection);
            await administrative.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", administrative);
            await create.ExecuteNonQueryAsync();
            var connection = new NpgsqlConnectionStringBuilder(administrativeConnection) { Database = name, Pooling = false }.ConnectionString;
            var database = new Database(administrativeConnection, name, connection);
            try
            {
                await using var context = database.Context();
                await context.Database.MigrateAsync();
                return database;
            }
            catch { await database.DisposeAsync(); throw; }
        }

        /** Drops only the generated test database after its contexts have been disposed. */
        public async ValueTask DisposeAsync()
        {
            await using var administrative = new NpgsqlConnection(administrativeConnection);
            await administrative.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", administrative);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
