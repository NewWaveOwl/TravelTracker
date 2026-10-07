using System.Text.Json;
using System.Text.Json.Serialization;
namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Validates the PATCH shape and preserves explicit nulls before business validation.
 */
public sealed class PatchAttractionRequestConverter : JsonConverter<PatchAttractionRequest>
{
    /**
     * Reads only known writable properties with their expected JSON types.
     * @param reader The incoming JSON reader.
     * @param typeToConvert The request type.
     * @param options The host serializer options.
     * @return A request containing cloned, presence-aware fields.
     */
    public override PatchAttractionRequest Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("PATCH must be an object.");
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.EnumerateObject())
        {
            var value = field.Value;
            var valid = field.Name switch
            {
                "name" or "address" or "description" => value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
                "rating" or "lat" or "lon" => value.ValueKind == JsonValueKind.Null ||
                    (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _)),
                "estimatedVisitMinutes" => value.ValueKind == JsonValueKind.Null ||
                    (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)),
                _ => false
            };
            if (!valid || !fields.TryAdd(field.Name, value.Clone()))
                throw new JsonException($"Unknown, repeated or invalid property: {field.Name}.");
        }
        return new(fields);
    }

    /**
     * Writes supplied fields without manufacturing omitted values.
     * @param writer The outgoing JSON writer.
     * @param value The presence-aware request.
     * @param options The host serializer options.
     */
    public override void Write(Utf8JsonWriter writer, PatchAttractionRequest value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var field in value.Fields)
        {
            writer.WritePropertyName(field.Key);
            field.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
