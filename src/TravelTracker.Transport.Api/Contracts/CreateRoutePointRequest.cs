using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Transport.Api.Contracts;

/**
 * Accepts a location and its role, without a scheduled journey.
 */
public sealed class CreateRoutePointRequest
{
    [Required, RegularExpression("^(accommodation|attraction|transport_stop)$")]
    public string? Type { get; init; }
    [Required]
    public string? Name { get; init; }
    [Required(AllowEmptyStrings = true)]
    public string? Address { get; init; } = string.Empty;
    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Lat { get; init; }
    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Lon { get; init; }
    public Guid? SourceId { get; init; }
}
