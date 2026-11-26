namespace TravelTracker.Frontend.Features.Trips;

/** map quality choices and the automatic default chosen from device capabilities. */
public static class MapQuality
{
    /** lets the device capabilities decide. */
    public const string Automatic = "automatic";

    /** full basemap with 3D layers and device-native resolution. */
    public const string Standard = "standard";

    /** flat basemap at one pixel per point with 3D layers hidden; markers and routes are unchanged. */
    public const string Low = "low";

    /** choices in display order. */
    public static readonly IReadOnlyList<string> Choices = [Automatic, Standard, Low];

    /**
     * names a choice for people to read in the map and settings controls.
     * @param choice Automatic, standard or low.
     * @return The visible label for the choice.
     */
    public static string Label(string choice) => choice switch
    {
        Standard => "Standard",
        Low => "Low",
        _ => "Automatic (recommended)"
    };

    /**
     * picks the quality a device should draw with. the default leans low: no WebGL, touch input, or little memory or few cores all point there. unreported values never force low.
     * @param preference The saved choice. Unknown values count as automatic.
     * @param capabilities What the browser reports about this device.
     * @return Standard or Low.
     */
    public static string Resolve(string preference, MapCapabilities capabilities)
    {
        if (preference is Standard or Low) return preference;
        var limited = capabilities.DeviceMemoryGigabytes is < 4 || capabilities.LogicalCores is <= 2;
        return !capabilities.WebGl || capabilities.CoarsePointer || limited ? Low : Standard;
    }
}
