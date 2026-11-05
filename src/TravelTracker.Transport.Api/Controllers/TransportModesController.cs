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
[Route("api/v1/transport-modes")]
public sealed class TransportModesController(ITransportModeService service) : ControllerBase
{

    /** Lists saved records for selection, without journey scheduling.
     * @param code Optional exact lowercase category.
     * @param name Optional case-insensitive name substring.
     * @param cancellationToken Cancels storage I/O.
     * @return Deterministically ordered public records, or 400 for invalid filters.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public async Task<ActionResult<IReadOnlyList<TransportModeRecord>>> GetAll(
        [FromQuery, RegularExpression("^(car|bus|train|pedestrian|bike|plane)$")] string? code,
        [FromQuery] string? name, CancellationToken cancellationToken)
    {
        if (Request.Query.Any(pair => (pair.Key.Equals("code", StringComparison.OrdinalIgnoreCase) ||
            pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase)) && pair.Value.Count != 1))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["query"] = ["Supply each filter once."] }));
        var records = await service.GetAllAsync(cancellationToken, code, name);
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
    public async Task<ActionResult<TransportModeRecord>> GetById(Guid id, CancellationToken cancellationToken)
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
    [ProducesResponseType(typeof(TransportModeRecord), StatusCodes.Status201Created)]
    public async Task<ActionResult<TransportModeRecord>> Create(CreateTransportModeRequest request, CancellationToken cancellationToken)
    {
        var saved = await service.CreateAsync(new TransportMode { Code = request.Code!, DisplayName = request.Name!, Description = request.Description }, cancellationToken);
        var response = ToResponse(saved);
        return CreatedAtAction(nameof(GetById), new { id = saved.TransportModeId.PublicId }, response);
    }

    /**
     * Keeps the agreed JSON fields while using the shared Domain entity.
     * @param record Entity returned by the shared service.
     * @return Its public HTTP response, including the existing name/coordinate aliases.
     */
    private static TransportModeRecord ToResponse(TransportMode record) => new(record.TransportModeId.PublicId, record.Code, record.DisplayName, record.Description);
}
