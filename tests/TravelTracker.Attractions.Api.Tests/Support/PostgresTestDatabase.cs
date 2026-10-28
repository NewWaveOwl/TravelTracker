using Npgsql;

namespace TravelTracker.Attractions.Api.Tests;

/**

 * Owns one disposable PostgreSQL database; never connects to a production database by default.

 */
internal sealed class PostgresTestDatabase : IDisposable
{
    private readonly string adminConnection;
    private readonly string name = "traveltracker_test_" + Guid.NewGuid().ToString("N");
    private int disposed;
    internal string ConnectionString { get; }

    /**

     * Creates an isolated database using an explicitly configured local/test PostgreSQL role.

     */
    public PostgresTestDatabase()
    {
        adminConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
            throw new InvalidOperationException("Set TRAVELTRACKER_TEST_POSTGRES to an isolated PostgreSQL instance with CREATEDB permission. SQLite is not a substitute.");
        using var admin = new NpgsqlConnection(adminConnection);
        admin.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        command.ExecuteNonQuery();
        var settings = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false };
        ConnectionString = settings.ConnectionString;
    }

    /**

     * Drops only this randomly named test database after its host has stopped.

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
