using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Authentication;
using TravelTracker.Attractions.Api.Contracts;
using TravelTracker.Attractions.Api.Access;
using TravelTracker.Attractions.Api.Services;
using TravelTracker.Attractions.Domain.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TravelTracker.Attractions.Api.Controllers;

/**
 * Exposes the independent attraction contract with scope and ownership enforcement.
 * @param service The request-scoped attraction application service.
 * @param caller The validated identity and live Users access context.
 */
[ApiController]
[Route("api/v1/attractions")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class AttractionsController(IAttractionService service, CallerContext caller) : ControllerBase
{
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
        // Query keys are already decoded and compared case-insensitively by ASP.NET Core.
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
     * and explicit nulls using TryGetProperty.
     */
    [HttpPatch("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttractionRecord>> Patch(Guid id, [FromBody] JsonElement patch)
    {
        var result = await service.UpdatePartialAsync(id, patch, HttpContext.RequestAborted);
        if (result.NotFound) return NotFound();
        if (result.Forbidden) return Forbid();
        if (result.Errors is not null && result.Errors.Any())
        {
            var ms = new ModelStateDictionary();
            foreach (var kv in result.Errors)
            {
                foreach (var e in kv.Value)
                    ms.AddModelError(kv.Key, e);
            }
            return ValidationProblem(ms);
        }
        return Ok(ToResponse(result.Updated!));
    }

    /**
     * Creates a resource from validated request fields.
     * @param request Writable fields accepted by the HTTP contract.
     * @return 201 with the saved representation and details Location; invalid input returns 400.
     */
    [HttpPost]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(typeof(AttractionRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<AttractionRecord>> Create(CreateAttractionRequest request)
    {
        if (request.Visibility == "shared" && !caller.IsAdministrator) return Forbid();
        var saved = await service.CreateAsync(new Attraction { IsShared = request.Visibility == "shared", Name = request.Name!, Description = request.Description,
            Rating = request.Rating, Address = request.Address!, Latitude = request.Lat!.Value,
            Longitude = request.Lon!.Value, EstimatedVisitMinutes = request.EstimatedVisitMinutes!.Value }, HttpContext.RequestAborted);
        var response = ToResponse(saved);
        return CreatedAtAction(nameof(GetById), new { id = saved.AttractionId.PublicId }, response);
    }

    /**
     * Keeps the agreed JSON fields while using the shared Domain entity.
     * @param record Entity returned by the shared service.
     * @return Its public HTTP response, including the existing name/coordinate aliases.
     */
    private static AttractionRecord ToResponse(Attraction record) => new(record.AttractionId.PublicId, record.Name, record.Description, record.Rating, record.Address, record.Latitude, record.Longitude, record.EstimatedVisitMinutes);
}
