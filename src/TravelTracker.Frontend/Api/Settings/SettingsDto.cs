namespace TravelTracker.Frontend.Api.Settings;

/**
 * public preference contract for the Settings service; carries no owner identity or revision.
 *
 * @param Theme The colour theme: system, light or dark.
 * @param DefaultMapLatitude The default map centre latitude, from -90 to 90.
 * @param DefaultMapLongitude The default map centre longitude, from -180 to 180.
 * @param DefaultMapZoom The default map zoom level, from 0 to 20.
 * @param PreferredTransportMode The preferred transport code.
 * @param TimeZone The IANA time zone identifier.
 */
public sealed record SettingsDto(string Theme, decimal DefaultMapLatitude, decimal DefaultMapLongitude, int DefaultMapZoom, string PreferredTransportMode, string TimeZone);
