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
    /**
     * Handles a failed request, distinguishing permanent input conflicts from transient outages.
     * @param context The failed HTTP request and its exception feature.
     * @return Completion after writing safe problem details, or ending an aborted request.
     */
    public static async Task HandleAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
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
            _ when exception is NpgsqlException { IsTransient: true } ||
                exception is DbUpdateException { InnerException: NpgsqlException { IsTransient: true } } => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        var title = status switch
        {
            400 => "The attraction contains invalid values.",
            403 => "The operation is not permitted.",
            408 => "The operation was canceled.",
            409 => "The attraction conflicts with an existing record.",
            503 => "Attraction storage is unavailable.",
            _ => "The request could not be completed."
        };
        await Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
}
