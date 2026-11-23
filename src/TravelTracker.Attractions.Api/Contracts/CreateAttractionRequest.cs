using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Accepts writable attraction fields with standard ASP.NET Core validation.
 */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAttractionRequest
{
    [Required, RegularExpression(@"^(private|shared)\z")]
    public string Visibility { get; init; } = "private";
    [Required, MaxLength(Attraction.NameMaxLength)]
    public string? Name { get; init; }
    [MaxLength(Attraction.DescriptionMaxLength)]
    public string? Description { get; init; }
    [Range(typeof(decimal), "0", "5")]
    public decimal? Rating { get; init; }
    [Required(AllowEmptyStrings = true), MaxLength(Attraction.AddressMaxLength)]
    public string? Address { get; init; } = string.Empty;
    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Lat { get; init; }
    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Lon { get; init; }
    [Required, Range(0, int.MaxValue)]
    public int? EstimatedVisitMinutes { get; init; }
}
