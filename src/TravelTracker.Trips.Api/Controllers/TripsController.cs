using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Api.Authentication;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Api.Contracts;

namespace TravelTracker.Trips.Api.Controllers;

/**
 * Exposes saving and own-trip retrieval. Every outcome maps to one documented status: 200 replaced, 201 created, 400 invalid
 * trip, 401 no valid caller, 403 account not permitted, 404 absent or not visible, 503 dependency unavailable.
 *
 * @param saveService Validates references and stores a ready trip.
 * @param readService Returns saved trips to their owner after the live standing check.
 * @param logger Records request-level refusals that the services do not already log.
 */
[ApiController]
[Route("api/v1/trips")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class TripsController(TripSaveService saveService, TripReadService readService, ILogger<TripsController> logger) : ControllerBase
{
    /**
     * Lists saved trips owned by the caller.
     * @return 200 with the trip summaries, 401 or 403 for the caller's account, or 503.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.TripsRead)]
    [ProducesResponseType(typeof(TripSummaryResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List()
    {
        var caller = CallerFromToken();
        if (caller is null) return TripReadFailure(TripReadStatus.Unauthorized);

        var (status, trips) = await readService.ListOwnedAsync(caller, HttpContext.RequestAborted);
        return status == TripReadStatus.Found
            ? Ok(trips.Select(trip => new TripSummaryResponse(trip.TripId, trip.Name, trip.Start, trip.End)).ToArray())
            : TripReadFailure(status);
    }

    /**
     * Retrieves one trip owned by the caller.
     *
     * @param id Public trip identifier; a malformed value is refused by model binding with 400.
     * @return 200 with the trip, 401 or 403 for the caller's account, 404 when absent or not the caller's, or 503.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.TripsRead)]
    [ProducesResponseType(typeof(TripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var caller = CallerFromToken();
        if (caller is null) return TripReadFailure(TripReadStatus.Unauthorized);

        var outcome = await readService.GetOwnedAsync(caller, id, HttpContext.RequestAborted);
        if (outcome.Status == TripReadStatus.Found)
            return Ok(TripWithEntityTag(outcome.Trip!));

        return TripReadFailure(outcome.Status);
    }

    private IActionResult TripReadFailure(TripReadStatus status) =>
        status switch
        {
            TripReadStatus.NotFound => NotFound(),
            TripReadStatus.AccountRejected => Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Account is not permitted to read trips."),
            TripReadStatus.Unauthorized => Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "A validated issuer and subject are required."),
            _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Account access could not be verified.")
        };

    /**
     * Validates every reference and trip rule, then stores the snapshot trip only when all of them pass.
     *
     * Needs both scopes: trips.write to save, and catalogue.read because the caller's token is forwarded to the
     * catalogue services, which refuse it without that scope. Checking it here reports a missing scope as 403
     * instead of letting valid references come back as 404.
     *
     * @param request Caller-writable trip values; the owner is taken from the validated token.
     * @return 201 with the saved trip and a Location header; 400 for invalid trip data; 401 or 403 for the account;
     * 404 when a referenced place is absent or not visible; 503 when a dependency cannot confirm the request.
     */
    [HttpPost]
    [Authorize(Policy = ScopePolicies.TripsWrite)]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(typeof(TripResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Create(CreateTripRequest request) => SaveAsync(request, null);

    /**
     * Replaces a caller-owned trip, keeping its identifier and replacing the complete itinerary atomically.
     *
     * @param id Public identifier of the trip to replace.
     * @param request Complete replacement using the same writable fields as create.
     * @param ifMatch The strong ETag from the trip the caller is editing.
     * @return 200 with the replacement and its ETag, 428 when If-Match is absent, 412 when it does not match,
     * or the same validation and authorization outcomes as create.
     */
    [HttpPut("{id}")]
    [Authorize(Policy = ScopePolicies.TripsWrite)]
    [Authorize(Policy = ScopePolicies.CatalogueRead)]
    [ProducesResponseType(typeof(TripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public Task<IActionResult> Replace(Guid id, CreateTripRequest request, [FromHeader(Name = "If-Match")] string? ifMatch) =>
        SaveAsync(request, id, ParseEntityTag(ifMatch));

    /**
     * Binds both write operations to the same caller, validation and error mapping.
     *
     * @param request Caller-writable trip values.
     * @param tripId Existing trip to replace, or null to create a new trip.
     * @param expectedRevision The requested revision, or null when If-Match is absent.
     * @return The saved representation or a validation, permission or dependency failure.
     */
    private async Task<IActionResult> SaveAsync(CreateTripRequest request, Guid? tripId, long? expectedRevision = null)
    {
        var caller = CallerFromToken();
        var bearerToken = BearerTokenFromHeader();
        if (caller is null || string.IsNullOrWhiteSpace(bearerToken))
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "A validated issuer, subject and access token are required.");

        for (var index = 0; index < request.Legs!.Count; index++)
            if (request.Legs[index] is null) ModelState.AddModelError($"legs[{index}]", "Each leg must be an object.");
        var draft = ModelState.IsValid ? ToDraft(request) : null;
        if (draft is null && tripId is null) return ValidationProblem(ModelState);

        var outcome = await saveService.SaveAsync(caller, bearerToken, draft, HttpContext.RequestAborted, tripId, expectedRevision);
        switch (outcome.Status)
        {
            case TripReferenceStatus.Ready:
                var trip = TripWithEntityTag(outcome.Trip!);
                return tripId is null ? CreatedAtAction(nameof(GetById), new { id = trip.TripId }, trip) : Ok(trip);
            case TripReferenceStatus.TripNotFound:
                return NotFound();
            case TripReferenceStatus.RevisionRequired:
                return Problem(statusCode: StatusCodes.Status428PreconditionRequired,
                    title: "An If-Match header naming the trip ETag is required to replace a trip.");
            case TripReferenceStatus.StaleRevision:
                return Problem(statusCode: StatusCodes.Status412PreconditionFailed,
                    title: "This trip changed since you opened it. Reopen it, review the changes and save again.");
            case TripReferenceStatus.InvalidTrip:
                foreach (var error in outcome.Errors) ModelState.AddModelError(error.Field, error.Message);
                return ValidationProblem(ModelState);
            case TripReferenceStatus.ReferenceRejected:
                return Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "A referenced place or transport mode is not available to you.",
                    extensions: new Dictionary<string, object?>
                    {
                        ["errors"] = outcome.Errors.GroupBy(error => error.Field)
                            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray())
                    });
            case TripReferenceStatus.AccountRejected:
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Account is not permitted to save trips.");
            case TripReferenceStatus.Unauthorized:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The access token was not accepted for this trip.");
            default:
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "A required service could not confirm this trip; nothing was saved.");
        }
    }

    /**
     * Builds the caller identity from the validated token claims, never from request input.
     *
     * @return The issuer and subject, or null when either claim is missing.
     */
    private CallerIdentity? CallerFromToken()
    {
        var issuer = User.FindFirst("iss")?.Value;
        var subject = User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ? null : new(issuer, subject);
    }

    /**
     * Reads the raw bearer credential that the reference lookups forward to the catalogue services.
     *
     * @return The token, or null when the Authorization header is not a bearer credential. The scheme name is case-insensitive.
     */
    private string? BearerTokenFromHeader()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    /**
     * Converts the request into the draft the services check. An unknown time zone id is reported next to the timeZone field.
     *
     * @param request The bound request, already checked for required fields.
     * @return The draft, or null when the time zone is unknown; ModelState then holds the field error.
     */
    private TripDraft? ToDraft(CreateTripRequest request)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone!);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("trip refused before lookups: the time zone id is not known to this host.");
            ModelState.AddModelError("timeZone", "timeZone must be a known IANA or Windows time zone id.");
            return null;
        }

        return new TripDraft(request.Name!, zone, request.Start!.Value, request.End!.Value, request.IsRoundTrip,
            request.Legs!.Select(leg => new TripLegDraft(leg.Sequence!.Value,
                new(leg.From!.PlaceId!.Value, leg.From.Type!),
                new(leg.To!.PlaceId!.Value, leg.To.Type!),
                leg.TransportModeId!.Value,
                leg.Departure!.Value,
                leg.Arrival!.Value)).ToList());
    }

    /**
     * Parses the exact strong ETag issued by the Trips API.
     * @param value Raw If-Match value.
     * @return The revision, null for an absent header, or a sentinel revision that never matches.
     */
    private static long? ParseEntityTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var tag = value.Trim();
        if (tag.Length < 3 || tag[0] != '"' || tag[^1] != '"' ||
            !long.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            revision <= 0 || tag != ToEntityTag(revision))
            return -1;
        return revision;
    }

    private static string ToEntityTag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    /**
     * Returns the public shape of the trip and sets the ETag for the same committed revision.
     * @param trip The saved trip.
     * @return The response without owner identity or revision in the body.
     */
    private TripResponse TripWithEntityTag(Trip trip)
    {
        Response.Headers.ETag = ToEntityTag(trip.Revision);
        return ToResponse(trip);
    }

    /**
     * Maps a stored trip to its public shape, keeping the snapshot values and omitting owner identity.
     *
     * @param trip The stored or ready trip.
     * @return The public response.
     */
    private static TripResponse ToResponse(Trip trip) => new(trip.TripId, trip.Name, trip.TimeZone.Id, trip.Start, trip.End, trip.IsRoundTrip,
        trip.Legs.Select(leg => new TripLegResponse(leg.Sequence,
            ToPlace(leg.From),
            ToPlace(leg.To),
            new(leg.Transport.TransportModeId, leg.Transport.Code, leg.Transport.Name),
            leg.Departure,
            leg.Arrival)).ToList());

    /**
     * Maps one place snapshot to its public shape.
     *
     * @param place The snapshot place.
     * @return The public place response.
     */
    private static TripPlaceResponse ToPlace(TripPlace place) =>
        new(place.PlaceId, place.Type, place.Name, place.Address, place.Latitude, place.Longitude);
}
