namespace TravelTracker.Users.Api.Contracts;

/** Own-profile data returned only to the authenticated account holder. */
public sealed record SelfProfileResponse(Guid PublicId, string? Username, string? Email, string? Address,
    string Role, string Status, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);
