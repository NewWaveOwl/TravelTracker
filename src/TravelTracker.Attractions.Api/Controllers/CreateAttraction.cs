using Microsoft.AspNetCore.Mvc;
using TravelTracker.Attractions.Api.Contracts;
namespace TravelTracker.Attractions.Api.Controllers;
public sealed partial class AttractionsController
{
    /**
     * Creates a resource and returns its details Location.
     * @param request Writable attraction fields.
     * @param cancellationToken Cancels the request.
     * @return 201 with saved details and their Location.
     */
    [HttpPost]
    public async Task<IActionResult> Create(CreateAttractionRequest request, CancellationToken cancellationToken)
    {
        var value = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = value.AttractionId }, AttractionResponseMapper.From(value));
    }
}
