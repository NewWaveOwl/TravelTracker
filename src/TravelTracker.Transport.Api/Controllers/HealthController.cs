using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Transport.Api.Controllers;

/**
 * Reports the health of the Transport API.
 */
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Returns the Transport API health status.
     * @return An HTTP 200 response containing the service health status.
     */
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "Transport" });
    }
}
