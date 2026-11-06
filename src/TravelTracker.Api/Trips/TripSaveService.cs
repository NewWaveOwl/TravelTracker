using Microsoft.Extensions.Logging;
using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * Saves a trip only after the reference validator reports it ready. Any other outcome leaves storage untouched.
 *
 * @param validator Checks accounts, references and trip rules and builds the snapshot trip.
 * @param trips Stores the snapshot trip.
 * @param logger Records whether each save was stored or skipped.
 */
public sealed class TripSaveService(TripReferenceValidator validator, ITripRepository trips, ILogger<TripSaveService> logger) {
    /**
     * Validates a draft and stores the resulting snapshot trip when it is ready.
     *
     * @param caller The issuer and subject from the caller's validated token.
     * @param bearerToken The caller's access token.
     * @param draft The trip as submitted.
     * @param cancellationToken Cancels the outbound lookups.
     * @return The validation outcome; the trip is stored only when its status is Ready.
     */
    public async Task<TripReferenceOutcome> SaveAsync(CallerIdentity caller, string bearerToken, TripDraft draft, CancellationToken cancellationToken) {
        var outcome = await validator.ValidateAsync(caller, bearerToken, draft, cancellationToken);
        if (outcome.Status != TripReferenceStatus.Ready || outcome.Trip is null) {
            logger.LogWarning("trip not stored: validation ended as {Status}; nothing was written.", outcome.Status);
            return outcome;
        }
        await trips.AddAsync(outcome.Trip);
        logger.LogInformation("trip stored with {Count} snapshot leg(s).", outcome.Trip.Legs.Count);
        return outcome;
    }
}
