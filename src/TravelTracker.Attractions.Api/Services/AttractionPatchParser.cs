using System.Text.Json;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Services;

/**
 * Applies presence-aware PATCH fields to a detached candidate without reading or writing storage.
 */
internal static class AttractionPatchParser
{
    /**
     * Rejects unknown or repeated fields and preserves omission versus explicit null.
     * @param existing The visible record to copy.
     * @param patch The requested JSON object.
     * @return A detached candidate or field validation errors.
     */
    internal static UpdateResult Apply(Attraction existing, JsonElement patch)
    {
        // Patch must be an object
        if (patch.ValueKind != JsonValueKind.Object)
        {
            return new UpdateResult(null, new Dictionary<string, string[]> { ["patch"] = new[] { "Request body must be a JSON object." } });
        }

        // Collect provided properties and detect unknowns
        var allowed = new HashSet<string>(new[] { "name", "description", "rating", "address", "lat", "lon", "estimatedVisitMinutes" }, StringComparer.OrdinalIgnoreCase);
        var provided = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in patch.EnumerateObject())
        {
            if (!allowed.Contains(prop.Name))
            {
                return new UpdateResult(null, new Dictionary<string, string[]> { [prop.Name] = new[] { "Unknown field." } });
            }
            if (!provided.TryAdd(prop.Name, prop.Value))
                return new UpdateResult(null, new Dictionary<string, string[]> { [prop.Name] = ["Duplicate field (case-insensitive)."] });
        }

        if (provided.Count == 0)
            return new UpdateResult(null, new Dictionary<string, string[]> { ["patch"] = new[] { "Empty update object is not allowed." } });

        // Start from a copy
        var candidate = new Attraction
        {
            OwnerIssuer = existing.OwnerIssuer, OwnerSubject = existing.OwnerSubject, IsShared = existing.IsShared,
            AttractionId = existing.AttractionId,
            Name = existing.Name,
            Description = existing.Description,
            Rating = existing.Rating,
            Address = existing.Address,
            Latitude = existing.Latitude,
            Longitude = existing.Longitude,
            EstimatedVisitMinutes = existing.EstimatedVisitMinutes
        };

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list)) { list = new List<string>(); errors[field] = list; }
            list.Add(message);
        }

        // Apply provided fields with validation rules per specification
        foreach (var kv in provided)
        {
            var name = kv.Key;
            var value = kv.Value;
            try
            {
                switch (name.ToLowerInvariant())
                {
                    case "name":
                        if (value.ValueKind == JsonValueKind.Null) { AddError("name", "Name cannot be null."); break; }
                        var nm = value.GetString() ?? string.Empty;
                        nm = nm.Trim();
                        if (string.IsNullOrEmpty(nm)) AddError("name", "Name must not be empty.");
                        else candidate.Name = nm;
                        break;
                    case "description":
                        if (value.ValueKind == JsonValueKind.Null) candidate.Description = null;
                        else candidate.Description = value.GetString();
                        break;
                    case "rating":
                        if (value.ValueKind == JsonValueKind.Null) { candidate.Rating = null; }
                        else
                        {
                            if (value.TryGetDecimal(out var dec))
                            {
                                if (dec < 0m || dec > 5m) AddError("rating", "Rating must be between 0 and 5.");
                                else candidate.Rating = dec;
                            }
                            else AddError("rating", "Rating must be a number.");
                        }
                        break;
                    case "address":
                        if (value.ValueKind == JsonValueKind.Null) { AddError("address", "Address cannot be null."); break; }
                        candidate.Address = value.GetString() ?? string.Empty;
                        break;
                    case "lat":
                        if (value.ValueKind == JsonValueKind.Null) { AddError("lat", "Latitude cannot be null."); break; }
                        if (value.TryGetDecimal(out var lat))
                        {
                            if (lat < -90m || lat > 90m) AddError("lat", "Latitude must be between -90 and 90.");
                            else candidate.Latitude = lat;
                        }
                        else AddError("lat", "Latitude must be a number.");
                        break;
                    case "lon":
                        if (value.ValueKind == JsonValueKind.Null) { AddError("lon", "Longitude cannot be null."); break; }
                        if (value.TryGetDecimal(out var lon))
                        {
                            if (lon < -180m || lon > 180m) AddError("lon", "Longitude must be between -180 and 180.");
                            else candidate.Longitude = lon;
                        }
                        else AddError("lon", "Longitude must be a number.");
                        break;
                    case "estimatedvisitminutes":
                        if (value.ValueKind == JsonValueKind.Null) { AddError("estimatedVisitMinutes", "EstimatedVisitMinutes cannot be null."); break; }
                        if (value.TryGetInt32(out var mins))
                        {
                            if (mins < 0) AddError("estimatedVisitMinutes", "EstimatedVisitMinutes must be non-negative.");
                            else candidate.EstimatedVisitMinutes = mins;
                        }
                        else AddError("estimatedVisitMinutes", "EstimatedVisitMinutes must be an integer.");
                        break;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or FormatException)
            {
                AddError(name, "Invalid value type.");
            }
        }

        if (errors.Count > 0)
        {
            var dict = errors.ToDictionary(k => k.Key, k => k.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            return new UpdateResult(null, new Dictionary<string, string[]>(dict, StringComparer.OrdinalIgnoreCase));
        }

        return new UpdateResult(candidate, null);
    }
}
