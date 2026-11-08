using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TravelTracker.Trips.Api.Errors;

/**
 * Maps unexpected failures to safe HTTP responses. Storage failures become 503 and are logged by exception type only,
 * because provider messages can contain connection details.
 */
public static class TripExceptionHandler
{
    /**
     * Converts a failed request to a safe problem response and logs the failure category.
     *
     * @param context The failed request and exception feature.
     * @return Completion after writing a problem response or ending an aborted request.
     */
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("TripFailures");
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogWarning("Trip request was canceled by the caller; status {StatusCode}.", 499);
            context.Response.StatusCode = 499;
            return;
        }

        var postgres = exception as PostgresException ?? exception?.InnerException as PostgresException;
        var status = exception switch
        {
            ValidationException => StatusCodes.Status400BadRequest,
            _ when postgres?.SqlState.StartsWith("22", StringComparison.Ordinal) == true => StatusCodes.Status400BadRequest,
            _ when postgres?.SqlState.StartsWith("23", StringComparison.Ordinal) == true => StatusCodes.Status400BadRequest,
            OperationCanceledException => StatusCodes.Status408RequestTimeout,
            NpgsqlException or DbUpdateException => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            StatusCodes.Status400BadRequest => "The trip contains values the store cannot accept; nothing was saved.",
            StatusCodes.Status408RequestTimeout => "The operation timed out.",
            StatusCodes.Status503ServiceUnavailable => "Trip storage is unavailable; nothing was saved.",
            _ => "The request could not be completed."
        };
        logger.Log(status >= 500 ? LogLevel.Error : LogLevel.Warning,
            "Trip request failed with HTTP {StatusCode} ({FailureType}); exception type {ExceptionType}.", status,
            status == StatusCodes.Status503ServiceUnavailable ? "dependency failure" : "request failure",
            exception?.GetType().Name ?? "none");
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
}
