using System.Text.Json;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Services;

/**
 * Applies presence-aware PATCH fields to a detached candidate without reading or writing storage.
 * Value rules (ranges, lengths) live in Attraction.Validate; this only checks shape and types.
 */
internal static class AttractionPatchParser
{
    private static readonly HashSet<string> AllowedFields = new(
        ["name", "description", "rating", "address", "lat", "lon", "estimatedVisitMinutes"], StringComparer.OrdinalIgnoreCase);

    /**
     * Rejects unknown or repeated fields and preserves omission versus explicit null.
     * @param existing The visible record to copy.
     * @param patch The requested JSON object.
     * @return A detached candidate or field validation errors.
     */
    internal static UpdateResult Apply(Attraction existing, JsonElement patch)
    {
        // patch must be an object
        if (patch.ValueKind != JsonValueKind.Object)
            return Failure("patch", "Request body must be a JSON object.");

        // collect provided properties and detect unknowns
        var provided = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in patch.EnumerateObject())
        {
            if (!AllowedFields.Contains(property.Name)) return Failure(property.Name, "Unknown field.");
            if (!provided.TryAdd(property.Name, property.Value)) return Failure(property.Name, "Duplicate field (case-insensitive).");
        }

        if (provided.Count == 0) return Failure("patch", "Empty update object is not allowed.");

        // start from a copy
        var candidate = new Attraction
        {
            OwnerIssuer = existing.OwnerIssuer,
            OwnerSubject = existing.OwnerSubject,
            IsShared = existing.IsShared,
            AttractionId = existing.AttractionId,
            Name = existing.Name,
            Description = existing.Description,
            Rating = existing.Rating,
            Address = existing.Address,
            Latitude = existing.Latitude,
            Longitude = existing.Longitude,
            EstimatedVisitMinutes = existing.EstimatedVisitMinutes
        };

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        void AddError(string field, string message) => errors[field] = [.. errors.GetValueOrDefault(field) ?? [], message];

        // apply provided fields; range and length rules are checked afterwards by Attraction.Validate
        foreach (var (name, value) in provided)
        {
            try
            {
                switch (name.ToLowerInvariant())
                {
                    case "name":
                        if (value.ValueKind == JsonValueKind.Null) AddError("name", "Name cannot be null.");
                        else candidate.Name = (value.GetString() ?? string.Empty).Trim();
                        break;
                    case "description":
                        candidate.Description = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
                        break;
                    case "rating":
                        if (value.ValueKind == JsonValueKind.Null) candidate.Rating = null;
                        else if (value.TryGetDecimal(out var rating)) candidate.Rating = rating;
                        else AddError("rating", "Rating must be a number.");
                        break;
                    case "address":
                        if (value.ValueKind == JsonValueKind.Null) AddError("address", "Address cannot be null.");
                        else candidate.Address = (value.GetString() ?? string.Empty).Trim();
                        break;
                    case "lat":
                        if (value.ValueKind == JsonValueKind.Null) AddError("lat", "Latitude cannot be null.");
                        else if (value.TryGetDecimal(out var latitude)) candidate.Latitude = latitude;
                        else AddError("lat", "Latitude must be a number.");
                        break;
                    case "lon":
                        if (value.ValueKind == JsonValueKind.Null) AddError("lon", "Longitude cannot be null.");
                        else if (value.TryGetDecimal(out var longitude)) candidate.Longitude = longitude;
                        else AddError("lon", "Longitude must be a number.");
                        break;
                    case "estimatedvisitminutes":
                        if (value.ValueKind == JsonValueKind.Null) AddError("estimatedVisitMinutes", "EstimatedVisitMinutes cannot be null.");
                        else if (value.TryGetInt32(out var minutes)) candidate.EstimatedVisitMinutes = minutes;
                        else AddError("estimatedVisitMinutes", "EstimatedVisitMinutes must be an integer.");
                        break;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                AddError(name, "Invalid value type.");
            }
        }

        return errors.Count > 0 ? new UpdateResult(null, errors) : new UpdateResult(candidate, null);
    }

    /**
     * Builds a single-error result.
     * @param field The offending field.
     * @param message The error message.
     * @return A result carrying only that error.
     */
    private static UpdateResult Failure(string field, string message) => new(null, new Dictionary<string, string[]> { [field] = [message] });
}
