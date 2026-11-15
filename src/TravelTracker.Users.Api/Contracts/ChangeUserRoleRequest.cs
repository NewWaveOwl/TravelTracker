using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** Writable role-change input; identity, standing, and profile data are never accepted here. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeUserRoleRequest
{
    [Required, RegularExpression(@"\A(traveller|administrator)\z")]
    public string Role { get; init; } = "";

    [Required, RegularExpression(@"\A(traveller|administrator)\z")]
    public string ExpectedRole { get; init; } = "";

    [Required, MaxLength(500)]
    public string Reason { get; init; } = "";
}
