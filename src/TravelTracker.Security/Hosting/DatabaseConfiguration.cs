using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace TravelTracker.Security.Hosting;

/** Validates the PostgreSQL settings shared by the API hosts without disclosing configured values. */
public static class DatabaseConfiguration
{
    /**
     * Reads a service connection before startup, with certificate verification required in Production.
     * @param builder The owning host and its effective server configuration.
     * @param service The service's configuration key.
     * @return The validated connection string, for use only by Npgsql.
     */
    public static string RequireDatabaseConnection(this WebApplicationBuilder builder, string service)
    {
        var key = $"ConnectionStrings:{service}";
        var production = builder.Environment.IsProduction();
        var value = builder.Configuration.GetConnectionString(service);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{key} is required.");

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(value);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidOperationException($"{key} must be a valid PostgreSQL connection string.");
        }

        if (string.IsNullOrWhiteSpace(connection.Host))
            throw new InvalidOperationException($"{key} must specify a database host.");
        if (connection.IncludeErrorDetail || connection.LogParameters)
            throw new InvalidOperationException($"{key} must leave Include Error Detail and Log Parameters disabled.");
        if (production && (connection.SslMode != SslMode.VerifyFull ||
            string.IsNullOrWhiteSpace(connection.Database) || string.IsNullOrWhiteSpace(connection.Username)))
            throw new InvalidOperationException($"{key} must specify Database, Username and SSL Mode=VerifyFull in Production.");
        if (production && builder.Configuration.ReadDatabaseFlag("Database:SeedExamples"))
            throw new InvalidOperationException("Database:SeedExamples must be disabled in Production.");
        return value;
    }

    /**
     * Reads an optional database flag without exposing malformed configuration values.
     * @param configuration The owning host configuration.
     * @param key The fixed name of the database setting.
     * @return Whether the flag is enabled.
     */
    public static bool ReadDatabaseFlag(this IConfiguration configuration, string key)
    {
        if (configuration[key] is not { } value) return false;
        if (bool.TryParse(value, out var enabled)) return enabled;
        throw new InvalidOperationException($"{key} must be true or false.");
    }

    /**
     * Runs startup migrations while keeping provider exceptions out of process output.
     * @param migrate Applies the service migrations and optional development seed data.
     * @return A task that completes after migrations are applied.
     */
    public static Task RunMigrationAsync(Func<Task> migrate) =>
        RunMigrationAsync(async () => { await migrate(); return true; });

    /**
     * Runs startup migrations with a safe startup error on failure.
     * @param migrate Applies the service migrations and returns a seed count.
     * @return The migration operation's result.
     */
    public static async Task<T> RunMigrationAsync<T>(Func<Task<T>> migrate)
    {
        try { return await migrate(); }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Database initialization failed ({failure.GetType().Name}). Check the connection, schema permissions and migrations.");
        }
    }
}
