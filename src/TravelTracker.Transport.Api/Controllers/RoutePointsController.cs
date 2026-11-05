using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Authentication;
using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Services;
using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Controllers;

/**
 * Exposes creation and lookup using the existing shared project structure.
 * @param service Shared application service for this resource.
 */
[ApiController]
[Route("api/v1/route-points")]
public sealed class RoutePointsController(IRoutePointService service) : ControllerBase
{

    /** Lists saved records for selection, without journey scheduling.
     * @param type Optional exact lowercase category.
     * @param name Optional case-insensitive name substring.
     * @param cancellationToken Cancels storage I/O.
     * @return Deterministically ordered public records, or 400 for invalid filters.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public async Task<ActionResult<IReadOnlyList<RoutePointRecord>>> GetAll(
        [FromQuery, RegularExpression("^(accommodation|attraction|transport_stop)$")] string? type,
        [FromQuery] string? name, CancellationToken cancellationToken)
    {
        if (Request.Query.Any(pair => (pair.Key.Equals("type", StringComparison.OrdinalIgnoreCase) ||
            pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase)) && pair.Value.Count != 1))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["query"] = ["Supply each filter once."] }));
        var records = await service.GetAllAsync(cancellationToken, type, name);
        return Ok(records.Select(ToResponse).ToArray());
    }

    /**
     * Reads a resource by its generated GUID.
     * @param id Identifier to find.
     * @param cancellationToken Cancels I/O.
     * @return 200 with its HTTP representation, or 404 when absent.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoutePointRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var record = await service.GetByIdAsync(id, cancellationToken);
        return record is null ? NotFound() : Ok(ToResponse(record));
    }

    /**
     * Creates a resource from validated request fields.
     * @param request Writable fields accepted by the HTTP contract.
     * @param cancellationToken Cancels I/O.
     * @return 201 with the saved representation and details Location; invalid input returns 400.
     */
    [HttpPost]
    [Authorize(Policy = ScopePolicies.CatalogueWrite)]
    [ProducesResponseType(typeof(RoutePointRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<RoutePointRecord>> Create(CreateRoutePointRequest request, CancellationToken cancellationToken)
    {
        var saved = await service.CreateAsync(new RoutePoint(default, request.Type!, request.Name!, request.Address!,
            request.Lat!.Value, request.Lon!.Value, request.SourceId), cancellationToken);
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
