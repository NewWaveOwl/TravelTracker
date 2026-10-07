using Microsoft.AspNetCore.Mvc;
using TravelTracker.Attractions.Api.Services;
namespace TravelTracker.Attractions.Api.Controllers;

/**
 * Binds catalogue requests and maps application results to HTTP.
 * @param service Scoped catalogue operations.
 */
[ApiController]
[Route("api/v1/attractions")]
public sealed partial class AttractionsController(IAttractionService service) : ControllerBase;
