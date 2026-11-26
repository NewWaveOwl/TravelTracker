using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Security.Authentication;
using TravelTracker.Attractions.Api.Contracts;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Domain.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace TravelTracker.Attractions.Api.Controllers;

/**
 * Exposes the independent attraction contract with scope and ownership enforcement.
 * @param service The request-scoped attraction application service.
 */
[ApiController]
[Route("api/v1/attractions")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class AttractionsController(IAttractionService service, ILogger<AttractionsController> logger) : ControllerBase
{
    /**
     * Publishes a visible location to the shared catalogue and acknowledges repeated shares.
     * @param id The existing location identifier.
     * @return Shared visibility, or 404 when the location is unavailable to the caller.
     */
    [HttpPost("{id:guid}/share")]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Share(Guid id) =>
        await service.ShareAsync(id, HttpContext.RequestAborted)
            ? Ok(new { visibility = "shared" }) : NotFound();

    /**
     * Reads a resource by its generated GUID.
     * @param id Identifier to find.
     * @return 200 with its HTTP representation, or 404 when absent.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttractionRecord>> GetById(Guid id)
    {
        var record = await service.GetByIdAsync(id, HttpContext.RequestAborted);
        return record is null ? NotFound() : Ok(ToResponse(record));
    }

    /**
     * Lists attractions, optionally filtered by a name substring (case-insensitive).
     * @param name Optional substring to filter attraction names.
     * @return 200 with the ordered list, or 400 field errors for repeated name parameters.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AttractionRecord>>> GetAll([FromQuery] string? name)
    {
        // query keys are already decoded and compared case-insensitively by ASP.NET Core.
        if (Request.Query.TryGetValue("name", out var names) && names.Count > 1)
        {
            ModelState.AddModelError("name", "The name query parameter must appear only once.");
            return ValidationProblem(ModelState);
        }

        var records = await service.GetAllAsync(name, HttpContext.RequestAborted);
        return Ok(records.Select(ToResponse).ToList());
    }

    /**
     * Applies a partial update described by a JSON object.
     * The request body is bound to a JsonElement so the service can detect field presence
     * and explicit nulls.
     * @param id Identifier of the attraction to update.
     * @param patch The partial update object.
     * @return 200 with the saved representation, 400 field errors, 403 when not writable, or 404 when absent.
     */
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPatch("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttractionRecord>> Patch(Guid id, [FromBody] JsonElement patch)
    {
        var result = await service.UpdatePartialAsync(id, patch, HttpContext.RequestAborted);
        if (result.NotFound) return NotFound();
        if (result.Forbidden)
        {
            logger.LogWarning("Attraction update refused: the caller may not write this attraction.");
            return Forbid();
        }
        if (result.Errors is not null && result.Errors.Any())
        {
            return FieldErrorsProblem(result.Errors);
        }
        return Ok(ToResponse(result.Updated!));
    }

    /**
     * Creates a resource from validated request fields.
     * @param request Writable fields accepted by the HTTP contract.
     * @return 201 with the saved representation and a Location header; invalid input returns 400 with field errors.
     */
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [HttpPost]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(typeof(AttractionRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<AttractionRecord>> Create(CreateAttractionRequest request)
    {
        var attraction = new Attraction
        {
            IsShared = request.Visibility == "shared",
            Name = request.Name!,
            Description = request.Description,
            Rating = request.Rating,
            Address = request.Address!,
            Latitude = request.Lat!.Value,
            Longitude = request.Lon!.Value,
            EstimatedVisitMinutes = request.EstimatedVisitMinutes!.Value
        };
        var errors = attraction.Validate();
        if (errors.Count != 0) return FieldErrorsProblem(errors);
        var saved = await service.CreateAsync(attraction, HttpContext.RequestAborted);
        var response = ToResponse(saved);
        return CreatedAtAction(nameof(GetById), new { id = saved.AttractionId.PublicId }, response);
    }

    /**
     * Keeps the agreed JSON fields while using the shared Domain entity.
     * @param record Entity returned by the shared service.
     * @return Its public HTTP response, including the existing name/coordinate aliases.
     */
    private static AttractionRecord ToResponse(Attraction record) => new(
        record.AttractionId.PublicId, record.Name, record.Description, record.Rating,
        record.Address, record.Latitude, record.Longitude, record.EstimatedVisitMinutes);

    /**
     * Reports field errors in the shared validation problem shape.
     * @param errors Field names mapped to their messages.
     * @return A 400 validation problem.
     */
    private ActionResult FieldErrorsProblem(Dictionary<string, string[]> errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                modelState.AddModelError(field, message);
        return ValidationProblem(modelState);
    }
}
