namespace TravelTracker.Frontend.Features.Settings;

/**
 * the preference values and ranges the Settings service accepts, mirrored so the form can reject bad input before a request.
 * the service stays authoritative; these rules only spare a round trip.
 */
public static class SettingsRules
{
    /** theme values the service accepts, in display order. */
    public static readonly string[] Themes = ["system", "light", "dark"];

    /** Transport codes the service accepts, in display order. */
    public static readonly string[] TransportModes = ["car", "bus", "train", "pedestrian", "bike", "plane"];

    /**
     * checks that the host knows an IANA time zone identifier.
     * @param timeZone The candidate identifier, already trimmed.
     * @return True when the identifier resolves to a time zone on this host.
     */
    public static bool IsKnownTimeZone(string timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone)) return false;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return true;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }
}
