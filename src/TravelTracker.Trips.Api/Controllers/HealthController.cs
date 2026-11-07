using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Trips.Api.Controllers;

/** Provides an intentionally public liveness endpoint that calls no dependency. */
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Reports the service identity. It does not probe PostgreSQL, the catalogue services or Users.
     *
     * @return 200 with the service status and name.
     */
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", service = "Trips" });
}
