using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Transport.Api.Contracts;

/**
 * Accepts a named transport entry, without journey dates or stop timings.
 */
public sealed class CreateTransportModeRequest
{
    [Required, RegularExpression("^(car|bus|train|pedestrian|bike|plane)$")]
    public string? Code { get; init; }
    [Required]
    public string? Name { get; init; }
    public string? Description { get; init; }
}
