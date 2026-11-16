using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Transport.Api.Controllers;

/**
 * Reports the health of the Transport service.
 */
[AllowAnonymous]
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Returns the Transport service health status.
     * @return 200 with the service identity.
     */
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "TravelTracker.Transport" });
}
