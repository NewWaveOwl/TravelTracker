using Microsoft.AspNetCore.Mvc;
using TravelTracker.Transport.Api.Services;
namespace TravelTracker.Transport.Api.Controllers;
/**
 * Binds TransportMode requests to application operations.
 * @param service Scoped resource service.
 */
[ApiController]
[Route("api/v1/transport-modes")]
public sealed partial class TransportModesController(ITransportModeService service) : ControllerBase;
