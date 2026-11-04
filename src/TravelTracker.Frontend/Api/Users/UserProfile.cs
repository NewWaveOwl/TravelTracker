namespace TravelTracker.Frontend.Api.Users;

/** Public Users profile; provider identifiers and bootstrap credentials never enter this DTO. */
public sealed record UserProfile(Guid UserId, string Email, string Role, string Status);
