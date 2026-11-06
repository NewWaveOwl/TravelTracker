using System.Globalization;
using TravelTracker.Frontend.Api.Settings;

namespace TravelTracker.Frontend.Features.Settings;

/** editable preference text, kept as typed so invalid input survives until the traveller corrects it. */
public sealed class SettingsDraft
{
    public string Theme { get; set; } = "system";
    public string Latitude { get; set; } = "0";
    public string Longitude { get; set; } = "0";
    public string Zoom { get; set; } = "2";
    public string TransportMode { get; set; } = "car";
    public string TimeZone { get; set; } = "UTC";

    /**
     * copies saved values into editable text without keeping a reference to the response.
     * @param settings The saved preference values.
     * @return A fresh draft that matches the saved values.
     */
    public static SettingsDraft From(SettingsDto settings) => new()
    {
        Theme = settings.Theme,
        Latitude = settings.DefaultMapLatitude.ToString(CultureInfo.InvariantCulture),
        Longitude = settings.DefaultMapLongitude.ToString(CultureInfo.InvariantCulture),
        Zoom = settings.DefaultMapZoom.ToString(CultureInfo.InvariantCulture),
        TransportMode = settings.PreferredTransportMode,
        TimeZone = settings.TimeZone,
    };

    /**
     * checks every field against the service rules before anything is sent.
     * @return Errors keyed by the JSON field name the service uses; empty when the draft can be saved.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (!SettingsRules.Themes.Contains(Theme, StringComparer.Ordinal)) errors["theme"] = ["Choose system, light or dark."];
        if (!Decimal(Latitude, -90, 90)) errors["defaultMapLatitude"] = ["Enter a latitude from -90 to 90."];
        if (!Decimal(Longitude, -180, 180)) errors["defaultMapLongitude"] = ["Enter a longitude from -180 to 180."];
        if (!int.TryParse(Zoom, NumberStyles.Integer, CultureInfo.InvariantCulture, out var zoom) || zoom is < 0 or > 20) errors["defaultMapZoom"] = ["Enter a whole zoom level from 0 to 20."];
        if (!SettingsRules.TransportModes.Contains(TransportMode, StringComparer.Ordinal)) errors["preferredTransportMode"] = ["Choose a transport mode from the list."];
        if (!SettingsRules.IsKnownTimeZone(TimeZone.Trim())) errors["timeZone"] = ["Enter an IANA time zone, such as America/Montreal or UTC."];
        return errors;
    }

    /**
     * builds the complete writable contract. call only after Validate returns no errors.
     * @return The preference values to replace on the service.
     */
    public SettingsDto ToSettings() => new(
        Theme,
        decimal.Parse(Latitude, NumberStyles.Float, CultureInfo.InvariantCulture),
        decimal.Parse(Longitude, NumberStyles.Float, CultureInfo.InvariantCulture),
        int.Parse(Zoom, NumberStyles.Integer, CultureInfo.InvariantCulture),
        TransportMode,
        TimeZone.Trim());

    static bool Decimal(string value, decimal min, decimal max) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number >= min && number <= max;
}
