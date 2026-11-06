using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Accommodations.Api.Controllers;

/** Provides an intentionally public liveness endpoint. */
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Reports the service identity without probing downstream dependencies.
     * @return 200 with the service status and name.
     */
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", service = "Accommodations" });
}
