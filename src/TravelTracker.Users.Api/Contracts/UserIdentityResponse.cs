namespace TravelTracker.Users.Api.Contracts;

/** Public identifier resolved for a backend-validated provider identity. */
public sealed record UserIdentityResponse(Guid UserId);
