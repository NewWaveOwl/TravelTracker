using Microsoft.AspNetCore.Mvc;
using TravelTracker.Attractions.Api.Contracts;
namespace TravelTracker.Attractions.Api.Controllers;
public sealed partial class AttractionsController
{
    /**
     * Returns a public catalogue view.
     * @param cancellationToken Cancels the request.
     * @return An ordered list or literal name matches.
     */
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var values = Request.Query["name"];
        if (values.Count > 1)
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["name"] = ["Supply name only once."] }));
        var records = values.Count == 0
            ? await service.GetAllAsync(cancellationToken)
            : await service.SearchAsync(values[0] ?? string.Empty, cancellationToken);
        return Ok(records.Select(AttractionResponseMapper.From));
    }
}
