using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** Username and password for registering or signing in. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CredentialsRequest
{
    [Required, MaxLength(32)] public string Username { get; init; } = "";
    [Required, MaxLength(128)] public string Password { get; init; } = "";
}
