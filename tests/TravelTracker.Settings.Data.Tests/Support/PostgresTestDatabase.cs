using Npgsql;

namespace TravelTracker.Settings.Data.Tests.Support;

/**
 * creates and later drops one random PostgreSQL database for a relational test.
 */
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string adminConnectionString;
    private readonly string databaseName = "traveltracker_settings_test_" + Guid.NewGuid().ToString("N");
    private int disposed;

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
     * @return a task that completes after the database is dropped.
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
