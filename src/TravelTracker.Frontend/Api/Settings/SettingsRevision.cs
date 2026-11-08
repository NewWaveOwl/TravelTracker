namespace TravelTracker.Frontend.Api.Settings;

/**
 * saved preferences together with the entity tag that must be echoed in If-Match on the next save.
 *
 * @param Settings The saved or default preference values.
 * @param ETag The strong entity tag naming the revision the values came from, such as "3".
 */
public sealed record SettingsRevision(SettingsDto Settings, string ETag);
