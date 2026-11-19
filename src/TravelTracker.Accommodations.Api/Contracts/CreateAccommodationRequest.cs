using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Api.Contracts;

/** Accepts only caller-writable accommodation values. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateAccommodationRequest
{
    [Required, RegularExpression(@"^(private|shared)\z")]
    public string Visibility { get; init; } = "private";

    [Required, StringLength(Accommodation.MaxNameLength)]
    public string? Name { get; init; }

    [Required(AllowEmptyStrings = true), StringLength(Accommodation.MaxAddressLength)]
    public string? Address { get; init; } = string.Empty;

    // named after the wire field so validation errors keep the public "lat" key
    [Required, Range(typeof(decimal), "-90", "90")]
    public decimal? Lat { get; init; }

    // named after the wire field so validation errors keep the public "lon" key
    [Required, Range(typeof(decimal), "-180", "180")]
    public decimal? Lon { get; init; }
}
