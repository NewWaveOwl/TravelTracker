using Npgsql;

namespace TravelTracker.Users.Data.Tests.Support;

/**
 * Owns one disposable PostgreSQL database for a relational test; never connects to a production database by default.
 */
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string databaseName = "traveltracker_users_test_" + Guid.NewGuid().ToString("N");
    private int disposed;

    public string ConnectionString { get; }

    /**
     * Creates an isolated database using an explicitly configured local/test PostgreSQL role.
     */
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
            Pooling = false,
        }.ConnectionString;
    }

    /**
     * Drops only this randomly named test database.
     * @return A task that completes after the database is dropped.
     */
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
