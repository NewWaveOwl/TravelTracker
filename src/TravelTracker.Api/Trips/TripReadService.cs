using TravelTracker.Trips.Data.Repositories;
using Microsoft.Extensions.Logging;
using TravelTracker.Api.References;
using TravelTracker.Data.Repositories;

namespace TravelTracker.Api.Trips;

/**
 * Returns one trip only to its owner. Account standing is checked live first: deactivated and unknown accounts
 * are refused, banned accounts may still read their own trips, and an unconfirmed standing fails closed.
 *
 * @param trips Reads owner-scoped trips.
 * @param usersStanding Checks account standing live from the Users service.
 * @param logger Records each refusal and fail-closed decision without identity values.
 */
public sealed class TripReadService(ITripRepository trips, UsersStandingClient usersStanding, ILogger<TripReadService> logger) {
    /**
     * Reads a trip for the caller, applying the live standing rule before the owner-scoped lookup.
     *
     * @param caller The issuer and subject from the caller's validated token.
     * @param tripId The public trip identifier from the route.
     * @param cancellationToken Cancels the lookups.
     * @return Found with the trip, or the category explaining why it was not returned.
     */
    public async Task<TripReadOutcome> GetOwnedAsync(CallerIdentity caller, Guid tripId, CancellationToken cancellationToken) {
        if (string.IsNullOrWhiteSpace(caller.Issuer) || string.IsNullOrWhiteSpace(caller.Subject)) {
            logger.LogWarning("trip read refused: the caller identity is incomplete.");
            return new(TripReadStatus.Unauthorized, null, "a validated caller identity is required.");
        }

        var standing = await usersStanding.GetStandingAsync(caller, cancellationToken);
        if (standing.Status == ReferenceStatus.NotFound) {
            logger.LogInformation("trip read refused: no account matches this sign-in.");
            return new(TripReadStatus.AccountRejected, null, "no account exists for this sign-in.");
        }
        if (standing.Status != ReferenceStatus.Found) {
            logger.LogWarning("trip read failed closed: account standing could not be confirmed ({Detail}).", standing.Detail);
            return new(TripReadStatus.DependencyUnavailable, null, $"account standing could not be confirmed: {standing.Detail}");
        }
        if (standing.Value!.Status == "deactivated") {
            logger.LogWarning("trip read refused: account standing is deactivated.");
            return new(TripReadStatus.AccountRejected, null, "deactivated accounts cannot read trips.");
        }

        var trip = await trips.GetOwnedByIdAsync(tripId, caller.Issuer, caller.Subject, cancellationToken);
        if (trip is null) {
            logger.LogInformation("trip read found no trip owned by this caller.");
            return new(TripReadStatus.NotFound, null, "no trip is visible to this caller.");
        }
        logger.LogInformation("trip read returned one owned trip.");
        return new(TripReadStatus.Found, trip, "trip is owned by this caller.");
    }
}
