namespace TravelTracker.Frontend.Api.Users;

/** Public Users profile; provider identifiers and bootstrap credentials never enter this DTO. */
public sealed record UserProfile(Guid UserId, string? Username, string Role, string Status);
