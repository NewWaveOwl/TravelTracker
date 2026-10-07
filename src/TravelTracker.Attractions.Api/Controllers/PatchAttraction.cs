using Microsoft.AspNetCore.Mvc;
using TravelTracker.Attractions.Api.Contracts;
namespace TravelTracker.Attractions.Api.Controllers;
public sealed partial class AttractionsController
{
    /**
     * Returns the result of a partial update.
     * @param id GUID text to update.
     * @param request Supplied writable fields.
     * @param cancellationToken Cancels the request.
     * @return Saved details, 400 for invalid input, or 404 when absent.
     */
    [HttpPatch("{id}")]
    public async Task<IActionResult> Patch(string id, PatchAttractionRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var parsed))
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["id"] = ["A GUID is required."] }));
        var value = await service.PatchAsync(parsed, request, cancellationToken);
        return value is null ? NotFound() : Ok(AttractionResponseMapper.From(value));
    }
}
