namespace TravelTracker.Settings.Api.Access;

/** holds only the validated identity for one request; settings owners never come from the client. */
public sealed class CallerContext
{
    public string Issuer { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}
