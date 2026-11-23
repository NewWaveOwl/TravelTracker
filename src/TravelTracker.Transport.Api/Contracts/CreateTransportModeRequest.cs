using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Transport.Api.Contracts;

/**
 * Accepts a named transport entry, without journey dates or stop timings.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateTransportModeRequest
{
    [Required, RegularExpression("^(car|bus|train|pedestrian|bike|plane)\\z")]
    public string? Code { get; init; }
    [Required, StringLength(200)]
    public string? Name { get; init; }
    [StringLength(1000)]
    public string? Description { get; init; }
}
