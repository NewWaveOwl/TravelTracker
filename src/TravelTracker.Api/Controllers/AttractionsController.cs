using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Contracts;
using TravelTracker.Api.Services;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Controllers;

/**
 * Exposes creation and lookup using the existing shared project structure.
 * @param service Shared application service for this resource.
 */
[ApiController]
[Route("api/v1/attractions")]
public sealed class AttractionsController(IAttractionService service) : ControllerBase
{
    /**
     * Reads a resource by its generated GUID.
     * @param id Identifier to find.
     * @return 200 with its HTTP representation, or 404 when absent.
     */
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AttractionRecord>> GetById(Guid id)
    {
        var record = await service.GetByIdAsync(id);
        return record is null ? NotFound() : Ok(ToResponse(record));
    }

    /**
     * Creates a resource from validated request fields.
     * @param request Writable fields accepted by the HTTP contract.
     * @return 201 with the saved representation and details Location; invalid input returns 400.
     */
    [HttpPost]
    [ProducesResponseType(typeof(AttractionRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<AttractionRecord>> Create(CreateAttractionRequest request)
    {
        var saved = await service.CreateAsync(new Attraction { Name = request.Name!, Description = request.Description,
            Rating = request.Rating, Address = request.Address!, Latitude = request.Lat!.Value,
            Longitude = request.Lon!.Value, EstimatedVisitMinutes = request.EstimatedVisitMinutes!.Value });
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
