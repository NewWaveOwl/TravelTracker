using TravelTracker.Attractions.Api.Models;
using TravelTracker.Attractions.Api.Contracts;
namespace TravelTracker.Attractions.Api.Validation;

/**
 * Owns the catalogue's complete field rules.
 */
public static class AttractionValidator
{
    /**
     * Validates create fields before constructing or inserting a record.
     * @param request Writable values, retaining missing required fields.
     */
    public static void Validate(CreateAttractionRequest request) => ValidateValues(
        request.Name, request.Rating, request.Address, request.Lat,
        request.Lon, request.EstimatedVisitMinutes);

    /**
     * Validates a complete candidate before an atomic replacement.
     * @param value The proposed immutable record.
     */
    public static void Validate(AttractionRecord value) => ValidateValues(
        value.Name, value.Rating, value.Address, value.Latitude,
        value.Longitude, value.EstimatedVisitMinutes);

    /**
     * Checks the agreed boundaries and reports errors together.
     * @param name Required nonblank name.
     * @param rating Optional score between zero and five.
     * @param address Address text; null is invalid.
     * @param lat Required latitude.
     * @param lon Required longitude.
     * @param minutes Required nonnegative visit duration.
     */
    private static void ValidateValues(string? name, decimal? rating, string? address,
        decimal? lat, decimal? lon, int? minutes)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(name)) errors["name"] = ["A nonblank name is required."];
        if (rating is < 0 or > 5) errors["rating"] = ["Rating must be between 0 and 5."];
        if (address is null) errors["address"] = ["Address must be text."];
        if (lat is null or < -90 or > 90) errors["lat"] = ["Latitude must be between -90 and 90."];
        if (lon is null or < -180 or > 180) errors["lon"] = ["Longitude must be between -180 and 180."];
        if (minutes is null or < 0) errors["estimatedVisitMinutes"] = ["A nonnegative duration is required."];
        if (errors.Count > 0) throw new InputValidationException(errors);
    }
}
