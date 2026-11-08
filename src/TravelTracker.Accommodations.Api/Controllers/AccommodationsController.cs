using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using TravelTracker.Accommodations.Api.Contracts;
using TravelTracker.Accommodations.Api.Services;
using TravelTracker.Accommodations.Domain.Models;
using TravelTracker.Api.Authentication;

namespace TravelTracker.Accommodations.Api.Controllers;

/** Exposes create, search and detail operations for visible accommodation records. */
[ApiController]
[Route("api/v1/accommodations")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class AccommodationsController(IAccommodationService service) : ControllerBase
{
    /**
     * Retrieves a visible accommodation by public identifier.
     * @param id Public accommodation identifier.
     * @return 200 with the record, or 404 when absent/private to another caller.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(typeof(AccommodationRecord), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccommodationRecord>> GetById(Guid id)
    {
        var record = await service.GetByIdAsync(id, HttpContext.RequestAborted);
        return record is null ? NotFound() : Ok(ToResponse(record));
    }

    /**
     * Lists visible accommodations, optionally matching a name substring.
     * @param name Optional literal substring to match case-insensitively.
     * @return 200 with ordered visible records, or 400 for repeated name parameters.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(typeof(List<AccommodationRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AccommodationRecord>>> GetAll([FromQuery] string? name)
    {
        if (Request.Query.TryGetValue("name", out var names) && names.Count > 1)
        {
            ModelState.AddModelError("name", "The name query parameter must appear only once.");
            return ValidationProblem(ModelState);
        }

        var records = await service.GetAllAsync(name, HttpContext.RequestAborted);
        return Ok(records.Select(ToResponse).ToList());
    }

    /**
     * Creates a private accommodation or an administrator-owned shared catalogue record.
     * @param request Caller-writable accommodation fields.
     * @return 201 with the created representation and Location header; invalid input returns 400.
     */
    [HttpPost]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(typeof(AccommodationRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<AccommodationRecord>> Create(CreateAccommodationRequest request)
    {
        var candidate = new Accommodation
        {
            IsShared = request.Visibility == "shared",
            Name = request.Name!,
            Address = request.Address!,
            Latitude = request.Lat!.Value,
            Longitude = request.Lon!.Value
        };
        var saved = await service.CreateAsync(candidate, HttpContext.RequestAborted);
        var response = ToResponse(saved);
        return CreatedAtAction(nameof(GetById), new { id = saved.AccommodationId }, response);
    }

    /**
     * Maps a domain record to the public wire shape without exposing owner identity.
     * @param record Saved domain accommodation.
     * @return Public response with `lat`/`lon` aliases and visibility.
     */
    private static AccommodationRecord ToResponse(Accommodation record) =>
        new(record.AccommodationId, record.Name, record.Address, record.Latitude, record.Longitude,
            record.IsShared ? "shared" : "private");
}
