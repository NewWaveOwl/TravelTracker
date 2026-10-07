using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Controllers;
public sealed partial class TransportModesController
{
    /**
     * Returns one public record.
     * @param id GUID text to look up.
     * @param cancellationToken Cancels the request.
     * @return Details, 400 for malformed IDs, or 404 when absent.
     */
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsed))
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["id"] = ["A GUID is required."] }));
        var value = await service.GetByIdAsync(parsed, cancellationToken);
        return value is null ? NotFound() : Ok(TransportModeResponseMapper.From(value));
    }
}
