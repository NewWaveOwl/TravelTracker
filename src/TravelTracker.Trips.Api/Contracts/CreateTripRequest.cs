using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Trips.Api.Contracts;

/**
 * accepts only caller-writable trip values. The owner comes from the validated token, never from the body.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTripRequest
{
    /** trip name, at most 200 characters. */
    [Required(AllowEmptyStrings = true)]
    [MaxLength(200)]
    public string? Name { get; init; }

    /** IANA or Windows time zone id the traveller entered times in, such as Europe/Paris. */
    [Required(AllowEmptyStrings = true)]
    public string? TimeZone { get; init; }

    /** trip start with its offset. */
    [Required]
    public DateTimeOffset? Start { get; init; }

    /** trip end with its offset. */
    [Required]
    public DateTimeOffset? End { get; init; }

    /** whether the trip returns to its starting accommodation. */
    public bool IsRoundTrip { get; init; }

    /** ordered legs, numbered from 1 with no gaps, at most 100. */
    [Required]
    [MaxLength(100)]
    public List<CreateTripLegRequest>? Legs { get; init; }
}
