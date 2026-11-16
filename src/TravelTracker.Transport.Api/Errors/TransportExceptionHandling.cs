using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Transport.Api.Errors;

/** Maps known storage failures to safe HTTP problems. */
public static class TransportExceptionHandling
{
    /**
     * Adds the service's storage failure boundary; failures are logged with the trace id and never leak details to clients.
     * @param app Independent host pipeline.
     * @return The configured pipeline.
     */
    public static IApplicationBuilder UseTransportExceptionHandling(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        try
        {
            await next(context);
        }
        catch (Exception error) when (!context.Response.HasStarted &&
            (error is NpgsqlException || error is DbUpdateException))
        {
            var databaseError = error as NpgsqlException ?? error.InnerException as NpgsqlException;
            var status = databaseError switch
            {
                PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation } => StatusCodes.Status400BadRequest,
                PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => StatusCodes.Status409Conflict,
                PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected } => StatusCodes.Status503ServiceUnavailable,
                { IsTransient: true } => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status500InternalServerError
            };
            // client outcomes are warnings; only server-side failures are errors. Only unexpected 500s keep the exception object, because provider messages can carry connection details.
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(TransportExceptionHandling))
                .Log(status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning,
                    status == StatusCodes.Status500InternalServerError ? error : null,
                    "Transport storage failed with status {Status} (sql state {SqlState}, exception type {ExceptionType}, trace {TraceId}).", status,
                    (databaseError as PostgresException)?.SqlState, error.GetType().Name, context.TraceIdentifier);
            await Results.Problem(statusCode: status, title: status switch
            {
                StatusCodes.Status400BadRequest => "The resource violates a storage constraint.",
                StatusCodes.Status409Conflict => "The resource conflicts with saved data.",
                StatusCodes.Status503ServiceUnavailable => "Transport storage is temporarily unavailable.",
                _ => "Transport storage failed."
            }).ExecuteAsync(context);
        }
    });
}
