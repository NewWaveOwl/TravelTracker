using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Trips.Api.Contracts;

/** A place reference: the public identifier and the kind of place it names. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTripPlaceRequest
{
    /** Public identifier of the place in its owning service. */
    [Required]
    public Guid? PlaceId { get; init; }

    /** accommodation, attraction or transport_stop. */
    [Required(AllowEmptyStrings = true)]
    public string? Type { get; init; }
}
