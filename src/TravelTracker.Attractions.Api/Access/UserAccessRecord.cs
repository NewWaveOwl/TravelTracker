namespace TravelTracker.Attractions.Api.Access;

/**
 * Describes live Users authorization data, not token-supplied role or standing claims.
 * @param Status active, banned or deactivated.
 * @param Role traveller or administrator.
 */
public sealed record UserAccessRecord(string Status, string Role);
