using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Api.Controllers;

/**
 * Reports the health of the combined API.
 */
[AllowAnonymous]
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Returns the shared API health status.
     * @return 200 with the service identity.
     */
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "TravelTracker" });
}
