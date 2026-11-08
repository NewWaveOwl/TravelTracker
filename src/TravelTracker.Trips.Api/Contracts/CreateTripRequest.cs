using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Trips.Api.Contracts;

/**
 * Accepts only caller-writable trip values. The owner comes from the validated token, never from the body.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTripRequest
{
    /** Trip name. */
    [Required(AllowEmptyStrings = true)]
    public string? Name { get; init; }

    /** IANA or Windows time zone id the traveller entered times in, such as Europe/Paris. */
    [Required(AllowEmptyStrings = true)]
    public string? TimeZone { get; init; }

    /** Trip start with its offset. */
    [Required]
    public DateTimeOffset? Start { get; init; }

    /** Trip end with its offset. */
    [Required]
    public DateTimeOffset? End { get; init; }

    /** Whether the trip returns to its starting accommodation. */
    public bool IsRoundTrip { get; init; }

    /** Ordered legs, numbered from 1 with no gaps. */
    [Required]
    public List<CreateTripLegRequest>? Legs { get; init; }
}
