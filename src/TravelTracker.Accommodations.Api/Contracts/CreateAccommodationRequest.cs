using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Accommodations.Api.Contracts;

/** Accepts only caller-writable accommodation values. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAccommodationRequest
{
    [Required, RegularExpression("^(private|shared)$")]
    public string Visibility { get; init; } = "private";

    [Required]
    public string? Name { get; init; }

    [Required(AllowEmptyStrings = true)]
    public string? Address { get; init; } = string.Empty;

    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Lat { get; init; }

    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Lon { get; init; }
}
