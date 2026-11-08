using System.Globalization;
using TravelTracker.Frontend.Api.Accommodations;

namespace TravelTracker.Frontend.Features.Accommodations;

/** Form-only accommodation values and validation, independent of HTTP and persistence. */
public sealed class AccommodationDraft
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Lat { get; set; } = "";
    public string Lon { get; set; } = "";
    public string Visibility { get; set; } = "private";

    /**
     * Checks required name, coordinate ranges and supported visibility before a request is sent.
     * @return Errors keyed by public request field name.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Enter an accommodation name."];
        if (!Coordinate(Lat, -90, 90)) errors["lat"] = ["Enter a latitude from −90 to 90."];
        if (!Coordinate(Lon, -180, 180)) errors["lon"] = ["Enter a longitude from −180 to 180."];
        if (Visibility is not ("private" or "shared")) errors["visibility"] = ["Choose private or shared visibility."];
        return errors;
    }

    /**
     * Builds the validated public create request.
     * @return Writable values with trimmed name and non-null address.
     */
    public CreateAccommodationRequest ToRequest() => new(Name.Trim(), Address, ParseCoordinate(Lat), ParseCoordinate(Lon), Visibility);

    static bool Coordinate(string text, decimal minimum, decimal maximum) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= minimum && value <= maximum;

    static decimal ParseCoordinate(string text) => decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
