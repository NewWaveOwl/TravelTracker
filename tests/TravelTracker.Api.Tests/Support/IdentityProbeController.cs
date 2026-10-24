using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Authentication;

namespace TravelTracker.Api.Tests;

/**
 * Exposes the authenticated subject only inside the isolated test host.
 */
[ApiController]
[Route("test/identity")]
public sealed class IdentityProbeController : ControllerBase
{
    /**
     * Returns the identity derived by production bearer validation.
     * @return The validated subject and issuer.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    public IActionResult Get() => Ok(new { subject = User.Identity?.Name, issuer = User.FindFirst("iss")?.Value });
}
