using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Attractions.Api.Controllers;

/**
 * Reports the liveness and identity of the independent Attractions API.
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
    public IActionResult Get() => Ok(new { status = "ok", service = "Attractions" });
}
