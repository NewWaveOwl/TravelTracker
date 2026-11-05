namespace TravelTracker.Users.Data.Repositories.Profile;

/** Indicates that a normalized username is already assigned to a different account. */
public sealed class UserProfileUsernameConflictException() : Exception("That username is already in use.");
