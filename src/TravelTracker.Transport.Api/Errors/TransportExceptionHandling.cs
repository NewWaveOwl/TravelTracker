using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Transport.Api.Errors;

/** Maps known storage failures to safe HTTP problems. */
public static class TransportExceptionHandling
{
    /** Adds the service's storage failure boundary.
     * @param app Independent host pipeline.
     * @return The configured pipeline.
     */
    public static IApplicationBuilder UseTransportExceptionHandling(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (Exception error) when (!context.Response.HasStarted &&
            (error is NpgsqlException || error is DbUpdateException))
        {
            var databaseError = error as NpgsqlException ?? error.InnerException as NpgsqlException;
            var status = databaseError switch
            {
                PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation } => 400,
                PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } => 409,
                { IsTransient: true } => 503,
                _ => 500
            };
            await Results.Problem(statusCode: status, title: status switch
            {
                400 => "The resource violates a storage constraint.",
                409 => "The resource conflicts with saved data.",
                503 => "Transport storage is temporarily unavailable.",
                _ => "Transport storage failed."
            }).ExecuteAsync(context);
        }
    });
}
