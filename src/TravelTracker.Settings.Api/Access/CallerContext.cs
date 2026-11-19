namespace TravelTracker.Settings.Api.Access;

/** holds only the validated identity for one request; settings owners never come from the client. */
public sealed class CallerContext
{
    /** the validated token issuer; empty until the access middleware sets it. */
    public string Issuer { get; set; } = string.Empty;
    /** the validated token subject; empty until the access middleware sets it. */
    public string Subject { get; set; } = string.Empty;
}
