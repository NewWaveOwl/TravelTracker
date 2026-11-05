namespace TravelTracker.Users.Api.Services;

/** Identifies the outcome of a self-profile application operation. */
public enum UserProfileResultKind
{
    Success,
    Forbidden,
    ValidationFailure,
    UsernameConflict,
    ConcurrencyConflict
}
