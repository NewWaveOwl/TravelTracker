using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using TravelTracker.Api.Authentication;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Api.TicketImports;
using TravelTracker.Trips.Data.TicketImports;

namespace TravelTracker.Trips.Api.Controllers;

/**
 * Exposes owner-only temporary PDF import; every operation leaves the trip unchanged.
 * @param imports Coordinates live access checks, local extraction and temporary source storage.
 */
[ApiController]
[RawPdfBody]
[Route("api/v1/trips/{tripId}/ticket-imports")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class TicketImportsController(TicketImportService imports) : ControllerBase
{
    /**
     * Reads one raw PDF into a review draft after authorizing its trip owner.
     * @param tripId Existing owned trip.
     * @param fileName Optional URI-encoded display basename.
     * @return 201 with proposed fields and a private source Location; safe 400/413/415/422/429 on invalid or unsupported input.
     */
    [HttpPost]
    [Authorize(Policy = ScopePolicies.TripsWrite)]
    [RequestSizeLimit(MemoryTicketImportStore.MaxFileBytes)]
    [ProducesResponseType(typeof(TicketImportDraftResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status408RequestTimeout)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload(Guid tripId, [FromHeader(Name = "X-Ticket-File-Name")] string? fileName)
    {
        PrivateResponse();
        var result = await imports.UploadAsync(Caller(), tripId, Request.Body, Request.ContentType, Request.ContentLength,
            fileName, HttpContext.RequestAborted);
        if (result.StatusCode != 201) return Failure(result.StatusCode, result.Error);
        return CreatedAtAction(nameof(Document), new { tripId, importId = result.Value!.ImportId }, result.Value);
    }

    /**
     * Downloads a still-private temporary source for comparison during review.
     * @param tripId Existing owned trip.
     * @param importId Opaque temporary document identifier.
     * @return 200 with a sandboxed PDF attachment or the documented access/absence response.
     */
    [HttpGet("{importId}/document")]
    [Authorize(Policy = ScopePolicies.TripsRead)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK, "application/pdf")]
    public async Task<IActionResult> Document(Guid tripId, Guid importId)
    {
        PrivateResponse();
        var result = await imports.GetDocumentAsync(Caller(), tripId, importId, HttpContext.RequestAborted);
        if (result.StatusCode != 200) return Failure(result.StatusCode, result.Error);
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        Response.OnCompleted(() =>
        {
            CryptographicOperations.ZeroMemory(result.Value!.Content);
            return Task.CompletedTask;
        });
        return File(result.Value!.Content, "application/pdf", result.Value.FileName);
    }

    /**
     * Removes one private temporary source when the active owner cancels.
     * @param tripId Existing owned trip.
     * @param importId Opaque temporary document identifier.
     * @return 204 when removed, 404 when absent/inaccessible, or a safe access failure.
     */
    [HttpDelete("{importId}")]
    [Authorize(Policy = ScopePolicies.TripsWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid tripId, Guid importId)
    {
        PrivateResponse();
        var result = await imports.CancelAsync(Caller(), tripId, importId, HttpContext.RequestAborted);
        return result.StatusCode == 204 ? NoContent() : Failure(result.StatusCode, result.Error);
    }

    /** Sets cache and sniffing restrictions for private document metadata as well as source bytes. */
    private void PrivateResponse()
    {
        Response.Headers.CacheControl = "no-store, private";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
    }

    /**
     * Reads only issuer/subject claims validated by the production bearer handler.
     * @return Validated identity or null when incomplete.
     */
    private CallerIdentity? Caller()
    {
        var issuer = User.FindFirst("iss")?.Value;
        var subject = User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ? null : new(issuer, subject);
    }

    /**
     * Maps safe outcomes without exposing whether another owner's private document exists.
     * @param status HTTP refusal.
     * @param error Safe public message.
     * @return An empty not-found response or problem details.
     */
    private IActionResult Failure(int status, string? error) => status == 404
        ? NotFound()
        : Problem(statusCode: status, title: error ?? "The ticket import could not be completed.");
}
