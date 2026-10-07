using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Controllers;
public sealed partial class RoutePointsController
{
    /**
     * Returns a public catalogue view.
     * @param cancellationToken Cancels the request.
     * @return Ordered public records.
     */
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok((await service.GetAllAsync(cancellationToken)).Select(RoutePointResponseMapper.From));
}
