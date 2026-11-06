using Microsoft.Extensions.Logging;
using TravelTracker.Api.References;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * Checks a draft trip before it may be saved: the caller's account must be live and active, every
 * referenced place and transport mode must be visible to the caller, and the itinerary must follow
 * the trip rules. The result carries snapshot values copied from those services; nothing is stored here.
 * Every refusal and every fail-closed path is logged without tokens or caller identity values.
 *
 * @param accommodations Reads accommodation places from the Accommodations service.
 * @param attractions Reads attraction places from the Attractions service.
 * @param routePoints Reads transport stop route points from the Transport service.
 * @param transportModes Reads transport modes from the Transport service.
 * @param usersStanding Checks account standing live from the Users service.
 * @param logger Records each refusal and fail-closed decision.
 */
public sealed class TripReferenceValidator(AccommodationReferenceClient accommodations, AttractionReferenceClient attractions,
    RoutePointReferenceClient routePoints, TransportModeReferenceClient transportModes, UsersStandingClient usersStanding,
    ILogger<TripReferenceValidator> logger) {
    private const string AccommodationType = "accommodation";
    private const string AttractionType = "attraction";
    private const string TransportStopType = "transport_stop";
    private static readonly HashSet<string> SupportedTypes = [AccommodationType, AttractionType, TransportStopType];

    /**
     * Validates a draft and builds its snapshot trip when every check passes.
     *
     * @param caller The issuer and subject from the caller's validated token.
     * @param bearerToken The caller's access token, forwarded to the catalogue services.
     * @param draft The trip as submitted.
     * @param cancellationToken Cancels the outbound lookups.
     * @return Ready with the snapshot trip, or the first blocking outcome with field-tied errors.
     */
    public async Task<TripReferenceOutcome> ValidateAsync(CallerIdentity caller, string bearerToken, TripDraft draft, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(caller.Issuer) || string.IsNullOrWhiteSpace(caller.Subject) || string.IsNullOrWhiteSpace(bearerToken)) {
            logger.LogWarning("trip check refused: the caller identity or access token is missing.");
            return Reject(TripReferenceStatus.Unauthorized, "a validated caller identity and access token are required.");
        }

        var typeErrors = CheckPlaceTypes(draft);
        if (typeErrors.Count > 0) {
            logger.LogWarning("trip check refused before any lookup: {Count} leg endpoint(s) name an unsupported place type.", typeErrors.Count);
            return new(TripReferenceStatus.InvalidTrip, null, typeErrors, "a leg names an unsupported place type.");
        }

        var standing = await usersStanding.GetStandingAsync(caller, cancellationToken);
        if (standing.Status == ReferenceStatus.NotFound) {
            logger.LogInformation("trip check refused: no account matches this sign-in.");
            return Reject(TripReferenceStatus.AccountRejected, "no account exists for this sign-in.");
        }
        if (standing.Status != ReferenceStatus.Found) {
            logger.LogWarning("trip check failed closed: account standing could not be confirmed ({Detail}).", standing.Detail);
            return Reject(TripReferenceStatus.DependencyUnavailable, $"account standing could not be confirmed: {standing.Detail}");
        }
        if (standing.Value!.Status != "active") {
            logger.LogWarning("trip check refused: account standing is {Status}, so trips cannot be saved.", standing.Value.Status);
            return Reject(TripReferenceStatus.AccountRejected, "banned and deactivated accounts cannot save trips.");
        }

        var errors = new List<TripError>();
        var legs = new List<TripLeg>();
        for (var index = 0; index < draft.Legs.Count; index++) {
            var leg = draft.Legs[index];
            var from = await ResolvePlaceAsync(leg.From, bearerToken, cancellationToken);
            var to = await ResolvePlaceAsync(leg.To, bearerToken, cancellationToken);
            var transport = await transportModes.GetModeAsync(leg.TransportModeId, bearerToken, cancellationToken);
            var blocking = BlockingFor(from, $"legs[{index}].from") ?? BlockingFor(to, $"legs[{index}].to")
                ?? BlockingFor(transport, $"legs[{index}].transport");
            if (blocking is not null) return blocking;
            Collect(from, $"legs[{index}].from", errors);
            Collect(to, $"legs[{index}].to", errors);
            Collect(transport, $"legs[{index}].transport", errors);
            if (from.Status == ReferenceStatus.Found && to.Status == ReferenceStatus.Found && transport.Status == ReferenceStatus.Found)
                legs.Add(new(leg.Sequence, from.Value!, to.Value!, transport.Value!, leg.Departure, leg.Arrival));
        }
        if (errors.Count > 0) {
            logger.LogWarning("trip check refused: {Fields} absent or not available to this caller.", string.Join(", ", errors.Select(error => error.Field)));
            return new(TripReferenceStatus.ReferenceRejected, null, errors, "one or more references are absent or not available to this caller.");
        }

        try {
            var trip = Trip.Create(draft.Name, draft.TimeZone, draft.Start, draft.End, draft.IsRoundTrip, legs);
            logger.LogInformation("trip check passed: {Count} leg(s) snapshotted and ready to save.", trip.Legs.Count);
            return new(TripReferenceStatus.Ready, trip, [], "trip snapshot is ready to save.");
        }
        catch (TripValidationException exception) {
            logger.LogWarning("trip check refused: the itinerary broke {Count} trip rule(s).", exception.Errors.Count);
            return new(TripReferenceStatus.InvalidTrip, null, exception.Errors, "the itinerary breaks a trip rule.");
        }
    }

    /**
     * Routes one requested place to the service that owns it.
     *
     * @param reference The requested place; its type must already be supported.
     * @param bearerToken The caller's access token.
     * @param cancellationToken Cancels the lookup.
     * @return The place snapshot result from the owning service.
     */
    private Task<ReferenceResult<TripPlace>> ResolvePlaceAsync(TripPlaceReference reference, string bearerToken, CancellationToken cancellationToken) =>
        reference.Type switch {
            AccommodationType => accommodations.GetPlaceAsync(reference.PlaceId, bearerToken, cancellationToken),
            AttractionType => attractions.GetPlaceAsync(reference.PlaceId, bearerToken, cancellationToken),
            _ => routePoints.GetTransportStopAsync(reference.PlaceId, bearerToken, cancellationToken)
        };

    /**
     * Finds every leg endpoint whose place type the trip cannot use, before any outbound call.
     *
     * @param draft The trip as submitted.
     * @return One error per unsupported endpoint; empty when all types are known.
     */
    private static List<TripError> CheckPlaceTypes(TripDraft draft) {
        var errors = new List<TripError>();
        for (var index = 0; index < draft.Legs.Count; index++) {
            if (!SupportedTypes.Contains(draft.Legs[index].From.Type))
                errors.Add(new($"legs[{index}].from", "place type must be accommodation, attraction or transport_stop."));
            if (!SupportedTypes.Contains(draft.Legs[index].To.Type))
                errors.Add(new($"legs[{index}].to", "place type must be accommodation, attraction or transport_stop."));
        }
        return errors;
    }

    /**
     * Stops the whole check for credential rejection or dependency failure, and logs the field that triggered it.
     * Absent references are not stops; they are collected so every missing field is reported.
     *
     * @param result The upstream lookup result.
     * @param field The field path of the reference being read, used in the log line.
     * @return The blocking outcome, or null when the lookup can continue.
     */
    private TripReferenceOutcome? BlockingFor<TValue>(ReferenceResult<TValue> result, string field) {
        switch (result.Status) {
            case ReferenceStatus.Unauthorized:
                logger.LogWarning("trip check stopped at {Field}: the catalogue rejected the caller's credential.", field);
                return Reject(TripReferenceStatus.Unauthorized, result.Detail);
            case ReferenceStatus.Unavailable:
                logger.LogWarning("trip check failed closed at {Field}: the owning service could not confirm it ({Detail}).", field, result.Detail);
                return Reject(TripReferenceStatus.DependencyUnavailable, result.Detail);
            default:
                return null;
        }
    }

    /**
     * Records a missing or inaccessible reference against its field. The message is the same for absent and
     * private records so the response never reveals which one it was.
     *
     * @param result The upstream lookup result.
     * @param field The field path that names the reference, such as legs[0].from.
     * @param errors The error list to append to.
     */
    private static void Collect<TValue>(ReferenceResult<TValue> result, string field, List<TripError> errors) {
        if (result.Status is ReferenceStatus.NotFound or ReferenceStatus.Forbidden)
            errors.Add(new(field, "this reference is absent or not available to you."));
    }

    /**
     * Builds an outcome that carries no trip and no field errors.
     *
     * @param status The blocking category.
     * @param detail Safe diagnostic text.
     * @return The outcome.
     */
    private static TripReferenceOutcome Reject(TripReferenceStatus status, string detail) => new(status, null, [], detail);
}
