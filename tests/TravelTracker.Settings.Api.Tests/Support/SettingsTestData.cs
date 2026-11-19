namespace TravelTracker.Settings.Api.Tests.Support;

/** shared preference bodies for the settings HTTP tests. */
internal static class SettingsTestData
{
    /**
     * the complete valid preference set used across the settings tests.
     * @return a JSON-serializable preference object.
     */
    internal static object ValidPreferences() => new
    {
        theme = "dark",
        defaultMapLatitude = 45.5m,
        defaultMapLongitude = -73.5m,
        defaultMapZoom = 12,
        preferredTransportMode = "train",
        timeZone = "America/Montreal"
    };
}
