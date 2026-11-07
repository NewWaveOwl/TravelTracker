using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Users.Api.Controllers;

/** Provides an intentionally public liveness endpoint that needs no database or identity configuration. */
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Reports the service identity without probing the database or provider.
     * @return 200 with the service status and name.
     */
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Get() => Ok(new { status = "ok", service = "Users" });
}
