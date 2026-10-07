using Microsoft.AspNetCore.Mvc;
using TravelTracker.WebApi.Contracts;
using TravelTracker.WebApi.Models;
using TravelTracker.WebApi.Services;

namespace TravelTracker.WebApi.Controllers;

/**
 * Exposes the basic create and read endpoints for this resource.
 * @param service Service handling resource creation and lookup.
 */
[ApiController]
[Route("api/v1/route-points")]
public sealed class RoutePointsController(IRoutePointService service) : ControllerBase
{
    /**
     * Handles GET /api/v1/route-points/{id}.
     * @param id Record identifier.
     * @return 200 with the record, or 404 when missing; malformed GUIDs are rejected by model binding.
     */
    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<RoutePointRecord> GetById(Guid id)
    {
        var record = service.GetById(id);
        return record is null ? NotFound() : Ok(record);
    }

    /**
     * Handles POST /api/v1/route-points.
     * @param request Fields validated by ASP.NET Core before this action runs.
     * @return 201 with the saved record and its details Location; invalid input returns 400.
     */
    [HttpPost]
    [ProducesResponseType(typeof(RoutePointRecord), StatusCodes.Status201Created)]
    public ActionResult<RoutePointRecord> Create(CreateRoutePointRequest request)
    {
        var record = service.Create(request);
        return CreatedAtAction(nameof(GetById), new { id = record.RoutePointId }, record);
    }
}
