using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Attractions.Api.Errors;

/**
 * Maps application and storage failures to safe HTTP outcomes without exposing provider details.
 */
public static class AttractionExceptionHandler
{
    /** The shared logger category for attraction request failures. */
    internal const string LoggerCategory = "AttractionFailures";

    /**
     * Handles a failed request, distinguishing permanent input conflicts from transient outages.
     * @param context The failed HTTP request and its exception feature.
     * @return Completion after writing safe problem details, or ending an aborted request.
     */
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Attraction request was canceled by the caller; status {StatusCode}.", 499);
            context.Response.StatusCode = 499;
            return;
        }
        var postgres = exception as PostgresException ?? exception?.InnerException as PostgresException;
        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            ValidationException => StatusCodes.Status400BadRequest,
            DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
            _ when postgres?.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected => StatusCodes.Status503ServiceUnavailable,
            _ when postgres?.SqlState == PostgresErrorCodes.UniqueViolation => StatusCodes.Status409Conflict,
            _ when postgres?.SqlState.StartsWith("23", StringComparison.Ordinal) == true => StatusCodes.Status400BadRequest,
            OperationCanceledException => StatusCodes.Status408RequestTimeout,
            _ when exception is NpgsqlException { IsTransient: true } ||
                exception is DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "The attraction contains invalid values.",
            StatusCodes.Status403Forbidden => "The operation is not permitted.",
            StatusCodes.Status408RequestTimeout => "The operation was canceled.",
            StatusCodes.Status409Conflict => "The attraction conflicts with an existing record.",
            StatusCodes.Status503ServiceUnavailable => "Attraction storage is unavailable.",
            _ => "The request could not be completed."
        };
        var failureKind = exception is OperationCanceledException ? "cancellation"
            : status == StatusCodes.Status409Conflict ? "conflict"
            : status == StatusCodes.Status503ServiceUnavailable ? "dependency failure"
            : "request failure";
        // Exception messages can carry credentials or submitted values.
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Attraction request failed with HTTP {StatusCode} ({FailureKind}, {ExceptionType}), trace {TraceIdentifier}.", status,
            failureKind, exception?.GetType().Name ?? "unknown", context.TraceIdentifier);
        await Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
}
