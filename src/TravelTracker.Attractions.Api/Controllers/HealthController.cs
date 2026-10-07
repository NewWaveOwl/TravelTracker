using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Attractions.Api.Controllers;

/**
 * Reports the health of the Attractions API.
 */
[ApiController]
[Route("health")]
public sealed class HealthController : ControllerBase
{
    /**
     * Returns the Attractions API health status.
     * @return An HTTP 200 response containing the service health status.
     */
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { status = "ok", service = "Attractions" });
    }
}
