using Microsoft.Extensions.Logging;
using TravelTracker.Trips.Data.Repositories;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * Saves a trip only after the reference validator reports it ready. Any other outcome leaves storage untouched,
 * and a failed write is logged by type and rethrown so the host can map it to a dependency failure.
 *
 * @param validator Checks accounts, references and trip rules and builds the snapshot trip.
 * @param trips Stores the snapshot trip for its validated owner.
 * @param logger Records whether each save was stored or skipped.
 */
public sealed class TripSaveService(TripReferenceValidator validator, ITripRepository trips, ILogger<TripSaveService> logger) {
    /**
     * Validates a draft and stores the resulting snapshot trip when it is ready.
     *
     * @param caller The issuer and subject from the caller's validated token; they become the trip owner.
     * @param bearerToken The caller's access token.
     * @param draft The submitted trip, or null when action-level request validation found errors.
     * @param cancellationToken Cancels the outbound lookups and the write.
     * @param tripId The owned trip to replace, or null to create a new trip.
     * @param expectedRevision Revision named by If-Match, or null if the header is absent.
     * @return The validation outcome; the trip is stored only when its status is Ready.
     */
    public async Task<TripReferenceOutcome> SaveAsync(CallerIdentity caller, string bearerToken, TripDraft? draft,
        CancellationToken cancellationToken, Guid? tripId = null, long? expectedRevision = null) {
        TripReferenceOutcome outcome;
        if (tripId is Guid existingId) {
            var refusal = await validator.CheckCallerAsync(caller, bearerToken, cancellationToken);
            if (refusal is not null) return refusal;
            if (draft is null) return InvalidDraft();

            var revision = await trips.GetOwnedRevisionAsync(existingId, caller.Issuer, caller.Subject, cancellationToken);
            if (revision is null) return TripNotFound();
            if (expectedRevision is null)
                return new(TripReferenceStatus.RevisionRequired, null, [], "If-Match is required to replace an owned trip.");
            if (expectedRevision != revision) return StaleRevision();

            outcome = await validator.ValidateReplacementAsync(bearerToken, draft, cancellationToken, existingId, revision.Value + 1);
        }
        else {
            if (draft is null) return InvalidDraft();
            outcome = await validator.ValidateAsync(caller, bearerToken, draft, cancellationToken);
        }
        if (outcome.Status != TripReferenceStatus.Ready || outcome.Trip is null) {
            logger.LogWarning("trip not stored: validation ended as {Status}; nothing was written.", outcome.Status);
            return outcome;
        }

        try {
            if (tripId is null)
                await trips.AddAsync(outcome.Trip, caller.Issuer, caller.Subject, cancellationToken);
            else {
                var replaced = await trips.ReplaceOwnedAsync(outcome.Trip, caller.Issuer, caller.Subject, expectedRevision!.Value, cancellationToken);
                if (replaced == TripReplacementStatus.NotFound) return TripNotFound();
                if (replaced == TripReplacementStatus.Stale) return StaleRevision();
            }
        }
        catch (Exception exception) {
            // log the exception type only; storage messages can carry connection details
            logger.LogError("trip not stored: the write failed ({FailureType}); the transaction was rolled back.", exception.GetType().Name);
            throw;
        }
        logger.LogInformation("trip stored with {Count} snapshot leg(s).", outcome.Trip.Legs.Count);
        return outcome;
    }

    /**
     * Reports the same outcome for absent and inaccessible trips.
     * @return A result with no trip data.
     */
    private static TripReferenceOutcome TripNotFound() =>
        new(TripReferenceStatus.TripNotFound, null, [], "the owned trip is absent.");

    /** Returns the validation outcome when the action already holds the field errors. */
    private static TripReferenceOutcome InvalidDraft() =>
        new(TripReferenceStatus.InvalidTrip, null, [], "the submitted draft is invalid.");

    /** Reports a stale revision without overwriting the stored trip. */
    private static TripReferenceOutcome StaleRevision() =>
        new(TripReferenceStatus.StaleRevision, null, [], "the owned trip has a newer revision.");
}
