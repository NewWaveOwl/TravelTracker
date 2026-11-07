using Npgsql;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * owns one disposable PostgreSQL database; never connects to a production database by default.
 */
internal sealed class PostgresTestDatabase : IDisposable
{
    private readonly string adminConnection;
    private readonly string name = "traveltracker_settings_api_test_" + Guid.NewGuid().ToString("N");
    private int disposed;

    /** the connection string for this isolated database. */
    internal string ConnectionString { get; }

    /**
     * creates an isolated database using the explicitly configured local or test PostgreSQL role.
     */
    public PostgresTestDatabase()
    {
        adminConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
            throw new InvalidOperationException("PostgreSQL settings tests require TRAVELTRACKER_TEST_POSTGRES with CREATEDB permission.");
        using var admin = new NpgsqlConnection(adminConnection);
        admin.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        command.ExecuteNonQuery();
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false }.ConnectionString;
    }

    /**
     * drops only this randomly named database after its host has stopped.
     */
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        using var admin = new NpgsqlConnection(adminConnection);
        admin.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
        command.ExecuteNonQuery();
    }
}
