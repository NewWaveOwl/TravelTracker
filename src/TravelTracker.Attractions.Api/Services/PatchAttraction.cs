using System.Text.Json;
using TravelTracker.Attractions.Api.Contracts;
using TravelTracker.Attractions.Api.Models;
using TravelTracker.Attractions.Api.Validation;
namespace TravelTracker.Attractions.Api.Services;
public sealed partial class AttractionService
{
    /**
     * Builds and validates a full replacement under the store's atomic update boundary.
     * @param id Attraction identifier.
     * @param request Presence-aware fields; only optional values may be cleared.
     * @param cancellationToken Cancels the request.
     * @return The saved record, or null when absent; invalid input leaves it unchanged.
     */
    public Task<AttractionRecord?> PatchAsync(Guid id, PatchAttractionRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.Count == 0)
            throw new InputValidationException(new() { ["body"] = ["Supply at least one writable field."] });
        foreach (var field in request.Fields)
            if (field.Value.ValueKind == JsonValueKind.Null && field.Key is not ("description" or "rating"))
                throw new InputValidationException(new() { [field.Key] = ["This field cannot be null."] });
        return repository.UpdateAsync(id, current =>
        {
            var fields = request.Fields;
            var candidate = current with
            {
                Name = fields.TryGetValue("name", out var name) ? name.GetString()!.Trim() : current.Name,
                Description = fields.TryGetValue("description", out var description) ? description.GetString() : current.Description,
                Rating = fields.TryGetValue("rating", out var rating)
                    ? rating.ValueKind == JsonValueKind.Null ? null : rating.GetDecimal() : current.Rating,
                Address = fields.TryGetValue("address", out var address) ? address.GetString()! : current.Address,
                Latitude = fields.TryGetValue("lat", out var lat) ? lat.GetDecimal() : current.Latitude,
                Longitude = fields.TryGetValue("lon", out var lon) ? lon.GetDecimal() : current.Longitude,
                EstimatedVisitMinutes = fields.TryGetValue("estimatedVisitMinutes", out var minutes)
                    ? minutes.GetInt32() : current.EstimatedVisitMinutes
            };
            AttractionValidator.Validate(candidate);
            return candidate;
        }, cancellationToken);
    }
}
