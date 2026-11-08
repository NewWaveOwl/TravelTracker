namespace TravelTracker.Settings.Data.Entities;

/** stores one account's preference values, keyed by its validated (iss, sub) pair. */
internal sealed class SettingsRow
{
    public string OwnerIssuer { get; set; } = string.Empty;
    public string OwnerSubject { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public decimal DefaultMapLatitude { get; set; }
    public decimal DefaultMapLongitude { get; set; }
    public int DefaultMapZoom { get; set; }
    public string PreferredTransportMode { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public long Revision { get; set; }
}
