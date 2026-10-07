using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Controllers;
public sealed partial class RoutePointsController
{
    /**
     * Creates a resource and returns its details Location.
     * @param request Writable resource fields.
     * @param cancellationToken Cancels the request.
     * @return 201 with saved details and their Location.
     */
    [HttpPost]
    public async Task<IActionResult> Create(CreateRoutePointRequest request, CancellationToken cancellationToken)
    {
        var value = await service.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = value.RoutePointId }, RoutePointResponseMapper.From(value));
    }
}
