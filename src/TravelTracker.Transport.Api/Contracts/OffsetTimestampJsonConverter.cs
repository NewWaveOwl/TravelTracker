using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace TravelTracker.Transport.Api.Contracts;

/**
 * Requires explicit-offset ISO timestamps and normalizes them to UTC.
 */
public sealed class OffsetTimestampJsonConverter : JsonConverter<DateTimeOffset>
{
    private static readonly Regex OffsetSuffix = new("(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant);
    /**
     * Binds and validates incoming JSON.
     * @param reader Incoming JSON.
     * @param typeToConvert Timestamp type.
     * @param options Serializer options.
     * @return A UTC instant with an explicit supplied offset.
     */
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A timestamp string is required.");
        var text = reader.GetString();
        if (text is null || !OffsetSuffix.IsMatch(text) || !reader.TryGetDateTimeOffset(out var value))
            throw new JsonException("Use an ISO timestamp with Z or an explicit UTC offset.");
        return value.ToUniversalTime();
    }
    /**
     * Serializes supplied values without adding missing fields.
     * @param writer Outgoing JSON.
     * @param value Timestamp to normalize.
     * @param options Serializer options.
     */
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime());
}
