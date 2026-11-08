using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Accommodations.Api.Errors;

/** Maps service/storage failures to safe HTTP responses without exposing internals. */
public static class AccommodationExceptionHandler
{
    /**
     * Converts a failed request to a safe problem response.
     * @param context The failed request and exception feature.
     * @return Completion after writing a problem response or ending an aborted request.
     */
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("AccommodationFailures");
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Accommodation request was canceled by the caller; status {StatusCode}.", 499);
            context.Response.StatusCode = 499;
            return;
        }

        var postgres = exception as PostgresException ?? exception?.InnerException as PostgresException;
        var status = exception switch
        {
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            ValidationException => StatusCodes.Status400BadRequest,
            _ when postgres?.SqlState == PostgresErrorCodes.UniqueViolation => StatusCodes.Status409Conflict,
            _ when postgres?.SqlState.StartsWith("23", StringComparison.Ordinal) == true => StatusCodes.Status400BadRequest,
            OperationCanceledException => StatusCodes.Status408RequestTimeout,
            NpgsqlException or DbUpdateException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "The accommodation contains invalid values.",
            StatusCodes.Status403Forbidden => "The operation is not permitted.",
            StatusCodes.Status408RequestTimeout => "The operation timed out.",
            StatusCodes.Status409Conflict => "The accommodation conflicts with an existing record.",
            StatusCodes.Status503ServiceUnavailable => "Accommodation storage is unavailable.",
            _ => "The request could not be completed."
        };
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Accommodation request failed with HTTP {StatusCode} ({FailureKind}).", status,
            status == StatusCodes.Status409Conflict ? "conflict" : status == StatusCodes.Status503ServiceUnavailable
                ? "dependency failure" : "request failure");
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
}
