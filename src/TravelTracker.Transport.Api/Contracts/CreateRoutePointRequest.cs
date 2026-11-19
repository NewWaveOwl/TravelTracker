using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Transport.Api.Contracts;

/**
 * Accepts a location and its role, without a scheduled journey.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateRoutePointRequest
{
    [Required, RegularExpression("^(accommodation|attraction|transport_stop)\\z")]
    public string? Type { get; init; }
    [Required, StringLength(200)]
    public string? Name { get; init; }
    [Required(AllowEmptyStrings = true), StringLength(500)]
    public string? Address { get; init; } = string.Empty;
    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Lat { get; init; }
    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Lon { get; init; }
    public Guid? SourceId { get; init; }
}
