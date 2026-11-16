using Npgsql;

namespace TravelTracker.Trips.Data.Tests.Support;

/**
 * creates and later drops one random PostgreSQL database for a relational test.
 */
internal sealed class PostgresTestDatabase : IAsyncDisposable
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
