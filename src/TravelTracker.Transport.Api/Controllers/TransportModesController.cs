using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Services;

namespace TravelTracker.Transport.Api.Controllers;

/**
 * Exposes the basic create and read endpoints for this resource.
 * @param service Service handling resource creation and lookup.
 */
[ApiController]
[Route("api/v1/transport-modes")]
public sealed class TransportModesController(ITransportModeService service) : ControllerBase
{
    /**
     * Handles GET /api/v1/transport-modes/{id}.
     * @param id Record identifier.
     * @return 200 with the record, or 404 when missing; malformed GUIDs are rejected by model binding.
     */
    [HttpGet("{id}")]
    public ActionResult<TransportModeRecord> GetById(Guid id)
    {
        var record = service.GetById(id);
        return record is null ? NotFound() : Ok(record);
    }

    /**
     * Handles POST /api/v1/transport-modes.
     * @param request Fields validated by ASP.NET Core before this action runs.
     * @return 201 with the saved record and its details Location; invalid input returns 400.
     */
    [HttpPost]
    public ActionResult<TransportModeRecord> Create(CreateTransportModeRequest request)
    {
        var record = service.Create(request);
        return CreatedAtAction(nameof(GetById), new { id = record.TransportModeId }, record);
    }
}
