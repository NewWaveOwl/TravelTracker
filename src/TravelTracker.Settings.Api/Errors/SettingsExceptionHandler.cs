using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Settings.Api.Errors;

/** maps service and storage failures to safe HTTP responses without exposing provider details. */
public static class SettingsExceptionHandler
{
    /** The shared logger category for Settings request failures. */
    internal const string LoggerCategory = "SettingsFailures";

    /**
     * converts a failed request to a safe problem response.
     * @param context the failed request and its exception feature.
     * @return completion after writing a problem response or ending an aborted request.
     */
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Settings request was canceled by the caller; status {StatusCode}.", 499);
            context.Response.StatusCode = 499;
            return;
        }

        var postgres = exception as PostgresException ?? exception?.InnerException as PostgresException;
        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            _ when postgres?.SqlState == PostgresErrorCodes.UniqueViolation => StatusCodes.Status409Conflict,
            _ when postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected => StatusCodes.Status503ServiceUnavailable,
            _ when postgres?.SqlState.StartsWith("23", StringComparison.Ordinal) == true => StatusCodes.Status400BadRequest,
            OperationCanceledException => StatusCodes.Status408RequestTimeout,
            NpgsqlException or DbUpdateException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "The settings contain invalid values.",
            StatusCodes.Status401Unauthorized => "A validated issuer and subject are required.",
            StatusCodes.Status408RequestTimeout => "The operation timed out.",
            StatusCodes.Status409Conflict => "The settings conflict with a concurrent save.",
            StatusCodes.Status503ServiceUnavailable => "Settings storage is unavailable.",
            _ => "The request could not be completed."
        };
        var failureKind = status switch
        {
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status503ServiceUnavailable => "dependency failure",
            _ => "request failure"
        };
        // Exception messages can carry credentials or submitted values.
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Settings request failed with HTTP {StatusCode} ({FailureKind}, {ExceptionType}).", status, failureKind,
            exception?.GetType().Name ?? "unknown");
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
}
