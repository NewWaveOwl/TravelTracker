using Npgsql;

namespace TravelTracker.Journey.Tests.Support;

/** owns one disposable, randomly named PostgreSQL database; never touches an existing one. */
internal sealed class PostgresDatabase : IDisposable
{
    private readonly string adminConnection;
    private readonly string name;
    private int disposed;

    /** the connection string for this isolated database. */
    internal string ConnectionString { get; }

    /**
     * creates the database using the role named by TRAVELTRACKER_TEST_POSTGRES, which needs CREATEDB.
     * @param label a short service name used in the database name.
     */
    internal PostgresDatabase(string label)
    {
        adminConnection = Environment.GetEnvironmentVariable("TRAVELTRACKER_TEST_POSTGRES") ??
            throw new InvalidOperationException("journey tests require TRAVELTRACKER_TEST_POSTGRES with CREATEDB permission.");
        name = $"traveltracker_journey_{label}_{Guid.NewGuid():N}";
        using var admin = new NpgsqlConnection(adminConnection);
        admin.Open();
        using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        command.ExecuteNonQuery();
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = name, Pooling = false }.ConnectionString;
    }

    /** drops only this database after its host has stopped. */
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        using var admin = new NpgsqlConnection(adminConnection);
        admin.Open();
        using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", admin);
        command.ExecuteNonQuery();
    }
}
