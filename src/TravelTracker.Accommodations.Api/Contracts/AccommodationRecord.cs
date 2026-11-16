using System.Text.Json.Serialization;

namespace TravelTracker.Accommodations.Api.Contracts;

/** Public accommodation representation; excludes owner and persistence details. */
public sealed record AccommodationRecord(Guid AccommodationId, string Name, string Address,
    [property: JsonPropertyName("lat")] decimal Latitude,
    [property: JsonPropertyName("lon")] decimal Longitude, string Visibility);
