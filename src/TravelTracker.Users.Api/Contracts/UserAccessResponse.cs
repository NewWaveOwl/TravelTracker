namespace TravelTracker.Users.Api.Contracts;

/** Minimal account standing for an authorized backend client. */
public sealed record UserAccessResponse(Guid UserId, string Status, string Role);
