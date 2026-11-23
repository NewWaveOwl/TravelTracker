using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TravelTracker.Security.Authentication;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Api.Contracts;

namespace TravelTracker.Trips.Api.Controllers;

/**
 * exposes saving and own-trip retrieval. Every outcome maps to one documented status: 200 replaced, 201 created, 400 invalid
 * trip, 401 no valid caller, 403 account not permitted, 404 absent or not visible, 503 dependency unavailable.
 *
 * @param saveService validates references and stores a ready trip.
 * @param readService returns saved trips to their owner after the live standing check.
 * @param logger records request-level refusals that the services do not already log.
 */
[ApiController]
[Route("api/v1/trips")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class TripsController(TripSaveService saveService, TripReadService readService, ILogger<TripsController> logger) : ControllerBase
{
    /** revision no stored trip carries; a malformed If-Match maps to it so the replace is refused as stale. */
    private const long UnmatchableRevision = -1;

    /**
     * lists saved trips owned by the caller.
     * @return 200 with the trip summaries, 401 or 403 for the caller's account, or 503.
     */
    [HttpGet]
    [Authorize(Policy = ScopePolicies.TripsRead)]
    [ProducesResponseType(typeof(TripSummaryResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List()
    {
        var caller = CallerFromToken();
        if (caller is null)
        {
            logger.LogWarning("trip list refused: the token carries no issuer or subject claim.");
            return TripReadFailure(TripReadStatus.Unauthorized);
        }

        var (status, trips) = await readService.ListOwnedAsync(caller, HttpContext.RequestAborted);
        return status == TripReadStatus.Found
            ? Ok(trips.Select(trip => new TripSummaryResponse(trip.TripId, trip.Name, trip.Start, trip.End)).ToArray())
            : TripReadFailure(status);
    }

    /**
     * retrieves one trip owned by the caller.
     *
     * @param id public trip identifier; a malformed value is refused by model binding with 400.
     * @return 200 with the trip, 401 or 403 for the caller's account, 404 when absent or not the caller's, or 503.
     */
    [HttpGet("{id}")]
    [Authorize(Policy = ScopePolicies.TripsRead)]
    [ProducesResponseType(typeof(TripResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var caller = CallerFromToken();
        if (caller is null)
        {
            logger.LogWarning("trip read refused: the token carries no issuer or subject claim.");
            return TripReadFailure(TripReadStatus.Unauthorized);
        }

        var outcome = await readService.GetOwnedAsync(caller, id, HttpContext.RequestAborted);
        if (outcome.Status == TripReadStatus.Found)
            return Ok(TripWithEntityTag(outcome.Trip!));

        return TripReadFailure(outcome.Status);
    }

    /**
     * maps a refused trip read or list to its one documented problem response.
     * @param status the refusal category.
     * @return the problem response for that category.
     */
    private IActionResult TripReadFailure(TripReadStatus status) =>
        status switch
        {
            TripReadStatus.NotFound => TripNotFound(),
            TripReadStatus.AccountRejected => Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Account is not permitted to read trips."),
            TripReadStatus.Unauthorized => Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "A validated issuer and subject are required."),
            _ => Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Account access could not be verified.")
        };

    /**
     * validates every reference and trip rule, then stores the snapshot trip only when all of them pass.
     *
     * needs both scopes: trips.write to save, and catalogue.read because the caller's token is forwarded to the
     * catalogue services, which refuse it without that scope. Checking it here reports a missing scope as 403
     * instead of letting valid references come back as 404.
     *
     * @param request caller-writable trip values; the owner is taken from the validated token.
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
     * replaces a caller-owned trip, keeping its identifier and replacing the complete itinerary atomically.
     *
     * @param id public identifier of the trip to replace.
     * @param request complete replacement using the same writable fields as create.
     * @param ifMatch the strong ETag from the trip the caller is editing.
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
     * binds both write operations to the same caller, validation and error mapping.
     *
     * @param request caller-writable trip values.
     * @param tripId existing trip to replace, or null to create a new trip.
     * @param expectedRevision the requested revision, or null when If-Match is absent.
     * @return the saved representation or a validation, permission or dependency failure.
     */
    private async Task<IActionResult> SaveAsync(CreateTripRequest request, Guid? tripId, long? expectedRevision = null)
    {
        var caller = CallerFromToken();
        var bearerToken = BearerTokenFromHeader();
        if (caller is null || string.IsNullOrWhiteSpace(bearerToken))
        {
            logger.LogWarning("trip save refused: the token carries no issuer, subject or bearer credential.");
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "A validated issuer, subject and access token are required.");
        }

        for (var index = 0; index < request.Legs!.Count; index++)
            if (request.Legs[index] is null) ModelState.AddModelError($"legs[{index}]", "Each leg must be an object.");
        if (request.Stops is not null)
            for (var index = 0; index < request.Stops.Count; index++)
                if (request.Stops[index] is null) ModelState.AddModelError($"stops[{index}]", "Each stop must be an object.");
        var draft = ModelState.IsValid ? ToDraft(request) : null;
        // on replace the save service checks account standing first, so a refused account never sees field errors.
        if (draft is null && tripId is null) return ValidationProblem(ModelState);

        var outcome = await saveService.SaveAsync(caller, bearerToken, draft, HttpContext.RequestAborted, tripId, expectedRevision);
        switch (outcome.Status)
        {
            case TripReferenceStatus.Ready:
                var trip = TripWithEntityTag(outcome.Trip!);
                return tripId is null ? CreatedAtAction(nameof(GetById), new { id = trip.TripId }, trip) : Ok(trip);
            case TripReferenceStatus.TripNotFound:
                return TripNotFound();
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
            case TripReferenceStatus.DependencyUnavailable:
            default:
                return Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "A required service could not confirm this trip; nothing was saved.");
        }
    }

    /**
     * builds the caller identity from the validated token claims, never from request input.
     *
     * @return the issuer and subject, or null when either claim is missing.
     */
    private CallerIdentity? CallerFromToken()
    {
        var issuer = User.FindFirst("iss")?.Value;
        var subject = User.FindFirst("sub")?.Value;
        return string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) ? null : new(issuer, subject);
    }

    /**
     * reads the raw bearer credential that the reference lookups forward to the catalogue services.
     *
     * @return the token, or null when the Authorization header is not a bearer credential. The scheme name is case-insensitive.
     */
    private string? BearerTokenFromHeader()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    /**
     * converts the request into the draft the services check. An unknown time zone id is reported next to the timeZone field.
     *
     * @param request the bound request, already checked for required fields.
     * @return the draft, or null when the time zone is unknown; ModelState then holds the field error.
     */
    private TripDraft? ToDraft(CreateTripRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out var zone))
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
                leg.Arrival!.Value)).ToList(),
            request.Stops?.Select(stop => new TripPlaceReference(stop.PlaceId!.Value, stop.Type!)).ToList());
    }

    /**
     * parses the exact strong ETag issued by the Trips API.
     * @param value raw If-Match value.
     * @return the revision, null for an absent header, or UnmatchableRevision when the value is malformed or not a strong tag.
     */
    private static long? ParseEntityTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var tag = value.Trim();
        if (tag.Length < 3 || tag[0] != '"' || tag[^1] != '"' ||
            !long.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ||
            revision <= 0 || tag != ToEntityTag(revision))
            return UnmatchableRevision;
        return revision;
    }

    /**
     * formats a revision as a strong entity tag.
     * @param revision the stored revision.
     * @return the quoted tag, for example "3".
     */
    private static string ToEntityTag(long revision) => $"\"{revision.ToString(CultureInfo.InvariantCulture)}\"";

    /**
     * answers an absent or not visible trip with the same problem body in every case, so existence never leaks.
     * @return a 404 problem response.
     */
    private ObjectResult TripNotFound() => Problem(statusCode: StatusCodes.Status404NotFound, title: "The trip was not found.");

    /**
     * returns the public shape of the trip and sets the ETag for the same committed revision.
     * @param trip the saved trip.
     * @return the response without owner identity or revision in the body.
     */
    private TripResponse TripWithEntityTag(Trip trip)
    {
        Response.Headers.ETag = ToEntityTag(trip.Revision);
        return ToResponse(trip);
    }

    /**
     * maps a stored trip to its public shape, keeping the snapshot values and omitting owner identity.
     *
     * @param trip the stored or ready trip.
     * @return the public response.
     */
    private static TripResponse ToResponse(Trip trip) => new(trip.TripId, trip.Name, trip.TimeZone.Id, trip.Start, trip.End, trip.IsRoundTrip,
        trip.Legs.Select(leg => new TripLegResponse(leg.Sequence,
            ToPlace(leg.From),
            ToPlace(leg.To),
            new(leg.Transport.TransportModeId, leg.Transport.Code, leg.Transport.Name),
            leg.Departure,
            leg.Arrival)).ToList(), trip.Stops.Select(ToPlace).ToList());

    /**
     * maps one place snapshot to its public shape.
     *
     * @param place the snapshot place.
     * @return the public place response.
     */
    private static TripPlaceResponse ToPlace(TripPlace place) =>
        new(place.PlaceId, place.Type, place.Name, place.Address, place.Latitude, place.Longitude);
}
