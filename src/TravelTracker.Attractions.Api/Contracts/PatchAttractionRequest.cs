using System.Text.Json;
using System.Text.Json.Serialization;
namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Keeps supplied PATCH fields distinct from fields omitted by the caller.
 * @param Fields The fields value.
 */
[JsonConverter(typeof(PatchAttractionRequestConverter))]
public sealed record PatchAttractionRequest(IReadOnlyDictionary<string, JsonElement> Fields);
