using System.Globalization;
using TravelTracker.Frontend.Api.Attractions;

namespace TravelTracker.Frontend.Features.Attractions;

/** Editable form values kept separately from the public service response, including preview-only visit uncertainty. */
public sealed class AttractionDraft
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Lat { get; set; } = "";
    public string Lon { get; set; } = "";
    public string EstimatedVisitMinutes { get; set; } = "";
    public string ApproximationMinutes { get; set; } = "";
    public string Rating { get; set; } = "";
    string description = "";
    bool descriptionTouched;
    public string Description { get => description; set { description = value; descriptionTouched = true; } }

    /**
     * Copies editable HTTP values without retaining a mutable reference to the response.
     * @param attraction The saved attraction.
     * @return A fresh form draft. Approximation is not supplied by the current API.
     */
    public static AttractionDraft From(AttractionDto attraction)
    {
        var draft = new AttractionDraft
        {
            Name = attraction.Name, Address = attraction.Address,
            Lat = attraction.Lat.ToString(CultureInfo.InvariantCulture), Lon = attraction.Lon.ToString(CultureInfo.InvariantCulture),
            EstimatedVisitMinutes = attraction.EstimatedVisitMinutes.ToString(CultureInfo.InvariantCulture),
            Rating = attraction.Rating?.ToString(CultureInfo.InvariantCulture) ?? "", Description = attraction.Description ?? "",
        };
        draft.descriptionTouched = false;
        return draft;
    }

    /**
     * Checks fields before sending a request; the service remains the authoritative validator.
     * @return Errors keyed by public field name, suitable for links to labelled inputs.
     */
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) errors["name"] = ["Enter an attraction name."];
        if (!Coordinate(Lat, -90, 90)) errors["lat"] = ["Enter a latitude from −90 to 90."];
        if (!Coordinate(Lon, -180, 180)) errors["lon"] = ["Enter a longitude from −180 to 180."];
        if (!WholeMinutes(EstimatedVisitMinutes)) errors["estimatedVisitMinutes"] = ["Enter a visit duration in whole minutes, 0 or more."];
        if (!string.IsNullOrWhiteSpace(ApproximationMinutes) && !WholeMinutes(ApproximationMinutes)) errors["approximationMinutes"] = ["Enter a duration approximation in whole minutes, 0 or more, or leave it blank."];
        if (!string.IsNullOrWhiteSpace(Rating) && (!decimal.TryParse(Rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) || rating is < 0 or > 5)) errors["rating"] = ["Enter a rating from 0 to 5, or leave it blank."];
        return errors;
    }

    /**
     * Creates the agreed public writable contract. Call only after validation succeeds.
     * @return Writable fields; preview uncertainty is deliberately absent from this API contract.
     */
    public Dictionary<string, object?> CreateBody() => new()
    {
        ["name"] = Name.Trim(), ["address"] = Address,
        ["lat"] = decimal.Parse(Lat, NumberStyles.Float, CultureInfo.InvariantCulture), ["lon"] = decimal.Parse(Lon, NumberStyles.Float, CultureInfo.InvariantCulture),
        ["estimatedVisitMinutes"] = int.Parse(EstimatedVisitMinutes, CultureInfo.InvariantCulture),
        ["rating"] = string.IsNullOrWhiteSpace(Rating) ? null : decimal.Parse(Rating, NumberStyles.Float, CultureInfo.InvariantCulture),
        ["description"] = string.IsNullOrWhiteSpace(Description) ? null : Description,
    };

    /**
     * Omits unchanged fields and uses explicit null only to clear the optional fields.
     * @param original The record on which the edit started.
     * @return A partial public PATCH body, never a response identifier or preview-only field.
     */
    public Dictionary<string, object?> PatchBody(AttractionDto original)
    {
        var originalBody = From(original).CreateBody();
        // Empty saved descriptions are distinct from an explicit null clear in PATCH.
        originalBody["description"] = original.Description;
        return CreateBody().Where(pair => (pair.Key != "description" || descriptionTouched) && !Equals(pair.Value, originalBody[pair.Key])).ToDictionary();
    }

    /**
     * Calculates a local preview without implying the approximation has been saved.
     * @return A nonnegative duration range, or null when one cannot be calculated.
     */
    public string? RangePreview() => WholeMinutes(EstimatedVisitMinutes) && WholeMinutes(ApproximationMinutes)
        ? $"{Math.Max(0L, (long)int.Parse(EstimatedVisitMinutes, CultureInfo.InvariantCulture) - int.Parse(ApproximationMinutes, CultureInfo.InvariantCulture))}–{(long)int.Parse(EstimatedVisitMinutes, CultureInfo.InvariantCulture) + int.Parse(ApproximationMinutes, CultureInfo.InvariantCulture)} minutes"
        : null;

    static bool Coordinate(string value, decimal min, decimal max) => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var coordinate) && coordinate >= min && coordinate <= max;
    static bool WholeMinutes(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes >= 0;
}
