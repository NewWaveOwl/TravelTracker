namespace TravelTracker.Settings.Domain.Models;

/**
 * Holds one traveller's preferences: theme, default map view, preferred transport and time zone.
 * Property initializers are the documented defaults returned before the first save.
 */
public sealed class UserSettings
{
    /** theme values the service accepts. */
    public static readonly string[] Themes = ["system", "light", "dark"];

    /** transport codes the service accepts; these match the Transport catalogue codes. */
    public static readonly string[] TransportModes = ["car", "bus", "train", "pedestrian", "bike", "plane"];

    /**
     * gets or sets the colour theme: system, light or dark.
     */
    public string Theme { get; set; } = "system";

    /**
     * gets or sets the default map centre latitude, between -90 and 90.
     */
    public decimal DefaultMapLatitude { get; set; }

    /**
     * gets or sets the default map centre longitude, between -180 and 180.
     */
    public decimal DefaultMapLongitude { get; set; }

    /**
     * gets or sets the default map zoom level, between 0 and 20.
     */
    public int DefaultMapZoom { get; set; } = 2;

    /**
     * gets or sets the preferred transport code, one of the supported modes.
     */
    public string PreferredTransportMode { get; set; } = "car";

    /**
     * gets or sets the IANA time zone identifier that trip times are shown in.
     */
    public string TimeZone { get; set; } = "UTC";

    /**
     * gets or sets the saved revision: 0 before the first save, then incremented by every accepted replace.
     * the HTTP ETag is derived from this value, so a stale client can be refused with 412.
     */
    public long Revision { get; set; }

    /**
     * checks every preference value against the supported ranges and lists.
     * @return field errors keyed by the JSON field name; empty when all values are valid.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (!Themes.Contains(Theme, StringComparer.Ordinal))
            errors["theme"] = ["Theme must be system, light or dark."];
        if (DefaultMapLatitude is < -90m or > 90m)
            errors["defaultMapLatitude"] = ["Latitude must be between -90 and 90."];
        if (DefaultMapLongitude is < -180m or > 180m)
            errors["defaultMapLongitude"] = ["Longitude must be between -180 and 180."];
        if (DefaultMapZoom is < 0 or > 20)
            errors["defaultMapZoom"] = ["Zoom must be between 0 and 20."];
        if (!TransportModes.Contains(PreferredTransportMode, StringComparer.Ordinal))
            errors["preferredTransportMode"] = ["Transport must be car, bus, train, pedestrian, bike or plane."];
        if (!IsKnownTimeZone(TimeZone))
            errors["timeZone"] = ["Time zone must be a known IANA identifier."];
        return errors;
    }

    /**
     * checks that the platform time zone database knows the identifier.
     * @param timeZone the candidate identifier.
     * @return whether the identifier resolves to a time zone.
     */
    private static bool IsKnownTimeZone(string timeZone)
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
