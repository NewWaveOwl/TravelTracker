namespace TravelTracker.Frontend.Api.Users;

/** The signed-in profile fields needed to display live administrator standing. */
public sealed record SelfUserProfile(Guid PublicId, string Role, string Status);
