using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Trips.Api.Contracts;

/** one requested leg. Places and the transport mode are referenced by their public identifiers. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTripLegRequest
{
    /** one-based position of the leg in the trip. */
    [Required]
    public int? Sequence { get; init; }

    /** where the journey starts. */
    [Required]
    public CreateTripPlaceRequest? From { get; init; }

    /** where the journey ends. */
    [Required]
    public CreateTripPlaceRequest? To { get; init; }

    /** public identifier of the transport mode in the Transport service. */
    [Required]
    public Guid? TransportModeId { get; init; }

    /** when the journey leaves From, with its offset. */
    [Required]
    public DateTimeOffset? Departure { get; init; }

    /** when the journey reaches To, with its offset. */
    [Required]
    public DateTimeOffset? Arrival { get; init; }
}
