using System.Text.Json.Serialization;

namespace TravelTracker.Users.Api.Contracts;

/** Owner-editable profile fields only; any other property (id, email, role, status, issuer, subject) is rejected with 400. */
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateProfileRequest(string? DisplayName, string? Address);
