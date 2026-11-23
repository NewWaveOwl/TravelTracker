namespace TravelTracker.Accommodations.Api.Access;

/** Contains only validated identity and live Users authorization data for one request. */
public sealed class CallerContext
{
    /** Gets or sets the validated token issuer. */
    public string Issuer { get; set; } = string.Empty;
    /** Gets or sets the validated token subject. */
    public string Subject { get; set; } = string.Empty;
    /** Gets or sets whether Users reports the administrator role. */
    public bool IsAdministrator { get; set; }
    /** Gets or sets whether Users reports the caller as banned. */
    public bool IsBanned { get; set; }
}
