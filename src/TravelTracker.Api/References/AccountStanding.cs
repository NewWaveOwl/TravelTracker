namespace TravelTracker.Api.References;

/**
 * Live account standing returned by the Users service.
 *
 * @param UserId Public account identifier.
 * @param Status active, banned or deactivated.
 * @param Role traveller or administrator.
 */
public sealed record AccountStanding(Guid UserId, string Status, string Role);
