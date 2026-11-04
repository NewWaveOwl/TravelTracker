using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** The authenticated Attractions service supplies a validated provider identity for a live standing lookup. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AccessRequest
{
    [Required, MaxLength(500)] public string Issuer { get; init; } = "";
    [Required, MaxLength(500)] public string Subject { get; init; } = "";
}
