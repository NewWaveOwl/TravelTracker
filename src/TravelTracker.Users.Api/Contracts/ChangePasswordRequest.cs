using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** The caller's current password and its replacement; neither value is ever echoed back. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangePasswordRequest
{
    public string? CurrentPassword { get; init; }

    public string? NewPassword { get; init; }
}
