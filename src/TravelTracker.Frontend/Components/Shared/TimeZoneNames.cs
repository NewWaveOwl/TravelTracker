namespace TravelTracker.Frontend.Components.Shared;

/** Time zone names this host knows, offered as suggestions; the service still decides what it accepts. */
public static class TimeZoneNames
{
    /** Every system zone id in ordinal order. */
    public static IReadOnlyList<string> All { get; } = TimeZoneInfo.GetSystemTimeZones().Select(zone => zone.Id).OrderBy(zoneId => zoneId, StringComparer.Ordinal).ToArray();
}
