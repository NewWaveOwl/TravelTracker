namespace TravelTracker.Settings.Api.Contracts;

/**
 * public settings representation; excludes owner identity and storage details.
 * @param Theme the colour theme: system, light or dark.
 * @param DefaultMapLatitude the default map centre latitude.
 * @param DefaultMapLongitude the default map centre longitude.
 * @param DefaultMapZoom the default map zoom level.
 * @param PreferredTransportMode the preferred transport code.
 * @param TimeZone the IANA time zone identifier.
 */
public sealed record SettingsRecord(string Theme, decimal DefaultMapLatitude, decimal DefaultMapLongitude,
    int DefaultMapZoom, string PreferredTransportMode, string TimeZone);
