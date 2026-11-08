using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Trips.Api.Contracts;

/** One requested leg. Places and the transport mode are referenced by their public identifiers. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTripLegRequest
{
    /** One-based position of the leg in the trip. */
    [Required]
    public int? Sequence { get; init; }

    /** Where the journey starts. */
    [Required]
    public CreateTripPlaceRequest? From { get; init; }

    /** Where the journey ends. */
    [Required]
    public CreateTripPlaceRequest? To { get; init; }

    /** Public identifier of the transport mode in the Transport service. */
    [Required]
    public Guid? TransportModeId { get; init; }

    /** When the journey leaves From, with its offset. */
    [Required]
    public DateTimeOffset? Departure { get; init; }

    /** When the journey reaches To, with its offset. */
    [Required]
    public DateTimeOffset? Arrival { get; init; }
}
