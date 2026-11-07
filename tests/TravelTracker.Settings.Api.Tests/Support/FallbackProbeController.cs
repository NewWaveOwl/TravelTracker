using Microsoft.AspNetCore.Mvc;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * exposes one action with no authorization metadata, so the shared fallback policy is what guards it.
 */
[ApiController]
[Route("test/fallback")]
public sealed class FallbackProbeController : ControllerBase
{
    /**
     * answers only after the fallback policy has accepted the caller.
     * @return 200 for an authorized caller.
     */
    [HttpGet]
    public IActionResult Get() => Ok();
}
