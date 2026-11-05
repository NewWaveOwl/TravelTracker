using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** Whitelists editable profile fields and distinguishes omitted values from explicit null. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateUserProfileRequest
{
    public string? Username { get; init { field = value; HasUsername = true; } }
    public string? Email { get; init { field = value; HasEmail = true; } }
    public string? Address { get; init { field = value; HasAddress = true; } }

    internal bool HasUsername { get; private set; }
    internal bool HasEmail { get; private set; }
    internal bool HasAddress { get; private set; }
}
