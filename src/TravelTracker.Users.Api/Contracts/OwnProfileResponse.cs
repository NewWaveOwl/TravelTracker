namespace TravelTracker.Users.Api.Contracts;

/** The caller's own profile; the address is returned here only and never in administrator listings. */
public sealed record OwnProfileResponse(Guid UserId, string Email, string DisplayName, string? Address, string Role, string Status);
