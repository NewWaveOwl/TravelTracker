using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Settings.Api.Contracts;

/**
 * accepts the complete writable preference set. range and list checks live in the domain so every rejection names its field.
 * unknown members, including any identity or owner field, are rejected before the service runs.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateSettingsRequest
{
    [Required(AllowEmptyStrings = true)]
    public string? Theme { get; init; }

    [Required]
    public decimal? DefaultMapLatitude { get; init; }

    [Required]
    public decimal? DefaultMapLongitude { get; init; }

    [Required]
    public int? DefaultMapZoom { get; init; }

    [Required(AllowEmptyStrings = true)]
    public string? PreferredTransportMode { get; init; }

    [Required(AllowEmptyStrings = true)]
    public string? TimeZone { get; init; }
}
