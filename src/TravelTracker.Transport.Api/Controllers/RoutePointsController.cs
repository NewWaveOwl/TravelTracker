using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Services;
namespace TravelTracker.Transport.Api.Controllers;
/**
 * Binds RoutePoint requests to application operations.
 * @param service Scoped resource service.
 */
[ApiController]
[Route("api/v1/route-points")]
public sealed partial class RoutePointsController(IRoutePointService service) : ControllerBase;
