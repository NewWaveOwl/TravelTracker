namespace TravelTracker.Api.Trips;

/**
 * The provider identity taken from a validated bearer token. Trips never accepts an owner from the request body.
 *
 * @param Issuer The validated token issuer.
 * @param Subject The validated token subject.
 */
public sealed record CallerIdentity(string Issuer, string Subject);
