using Microsoft.AspNetCore.Authorization;
using TravelTracker.Api.Authentication;
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
[Route("api/v1/route-points")]
public sealed class RoutePointsController(IRoutePointService service) : ControllerBase
{
    /**
     * Reads a resource by its generated GUID.
     * @param id Identifier to find.
     * @return 200 with its HTTP representation, or 404 when absent.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoutePointRecord>> GetById(Guid id)
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
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(typeof(RoutePointRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<RoutePointRecord>> Create(CreateRoutePointRequest request)
    {
        var saved = await service.CreateAsync(new RoutePoint(default, request.Type!, request.Name!, request.Address!,
            request.Lat!.Value, request.Lon!.Value, request.SourceId));
        var response = ToResponse(saved);
        return CreatedAtAction(nameof(GetById), new { id = saved.RoutePointId.PublicId }, response);
    }

    /**
     * Keeps the agreed JSON fields while using the shared Domain entity.
     * @param record Entity returned by the shared service.
     * @return Its public HTTP response, including the existing name/coordinate aliases.
     */
    private static RoutePointRecord ToResponse(RoutePoint record) => new(record.RoutePointId.PublicId, record.Type, record.Name, record.Address, record.Latitude, record.Longitude, record.SourceId);
}
