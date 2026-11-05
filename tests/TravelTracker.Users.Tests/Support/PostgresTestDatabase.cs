using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using TravelTracker.Users.Data;
using TravelTracker.Users.Data.Repositories;

namespace TravelTracker.Users.Tests.Support;

/** Owns one migrated, uniquely named disposable PostgreSQL database per test. */
internal sealed class PostgresTestDatabase(string administrativeConnection, string name, string connection) : IAsyncDisposable
{
    public string ConnectionString => connection;
    public UsersDbContext Context() => new(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options);
    public EfUserRepository Repository(TimeProvider? clock = null) => new(new PooledDbContextFactory<UsersDbContext>(new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(connection).Options), clock);

    /**
     * Creates and migrates an isolated database using an explicitly supplied nonproduction connection.
     * @return The per-test database; failed migrations drop it before propagating.
     */
    public static async Task<PostgresTestDatabase> CreateAsync()
    {
        var administrativeConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
            throw new InvalidOperationException("Set TRAVELTRACKER_TEST_POSTGRES to an isolated PostgreSQL instance with CREATEDB permission.");
        var name = "traveltracker_users_test_" + Guid.NewGuid().ToString("N");
        await using var administrative = new NpgsqlConnection(administrativeConnection);
        await administrative.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", administrative);
        await create.ExecuteNonQueryAsync();
        var connection = new NpgsqlConnectionStringBuilder(administrativeConnection) { Database = name, Pooling = false }.ConnectionString;
        var database = new PostgresTestDatabase(administrativeConnection, name, connection);
        try { await using var context = database.Context(); await context.Database.MigrateAsync(); return database; }
        catch { await database.DisposeAsync(); throw; }
    }

    /** Drops only this generated test database after its contexts are disposed. */
    public async ValueTask DisposeAsync()
    {
        await using var administrative = new NpgsqlConnection(administrativeConnection);
        await administrative.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", administrative);
        await drop.ExecuteNonQueryAsync();
    }
}
