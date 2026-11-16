namespace TravelTracker.Api.References;

/**
 * Live account standing returned by the Users service.
 *
 * @param UserId Public account identifier.
 * @param Status active, banned or deactivated.
 * @param Role traveller or administrator.
 */
public sealed record AccountStanding(Guid UserId, string Status, string Role)
{
    /** The Users contract value for an account in good standing. */
    public const string Active = "active";

    /** The Users contract value for a banned account. */
    public const string Banned = "banned";

    /** The Users contract value for a deactivated account. */
    public const string Deactivated = "deactivated";

    /** The Users contract value for the traveller role. */
    public const string Traveller = "traveller";

    /** The Users contract value for the administrator role. */
    public const string Administrator = "administrator";
}
