using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Services;
namespace TravelTracker.Transport.Api.Controllers;
/**
 * Binds TransportRoute requests to application operations.
 * @param service Scoped resource service.
 */
[ApiController]
[Route("api/v1/transport-routes")]
public sealed partial class TransportRoutesController(ITransportRouteService service) : ControllerBase;
