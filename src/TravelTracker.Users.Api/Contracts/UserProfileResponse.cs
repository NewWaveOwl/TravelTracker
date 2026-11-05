namespace TravelTracker.Users.Api.Contracts;

/** Public account response, omitting provider subject, issuer, credentials and persistence internals. */
public sealed record UserProfileResponse(Guid UserId, string? Email, string Role, string Status);
