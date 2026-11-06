namespace TravelTracker.Settings.Api.Access;

/**
 * describes the live Users standing and role resolved for one validated identity.
 * @param Status active, banned or deactivated.
 * @param Role traveller or administrator.
 */
public sealed record UserAccessRecord(string Status, string Role);
