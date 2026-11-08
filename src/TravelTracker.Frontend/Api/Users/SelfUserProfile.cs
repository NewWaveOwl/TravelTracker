namespace TravelTracker.Frontend.Api.Users;

/**
 * The signed-in caller's public profile returned by the Users API.
 * @param PublicId Public identifier for the caller's local TravelTracker account.
 * @param Role Server-owned application role.
 * @param Status Server-owned account status.
 * @param Username Caller-editable username, when provided.
 * @param Email Caller-editable contact email, distinct from provider-verified identity.
 * @param Address Caller-editable optional address.
 * @param CreatedAt Account creation time.
 * @param UpdatedAt Most recent profile update time, when one exists.
 */
public sealed record SelfUserProfile(Guid PublicId, string Role, string Status, string? Username = null, string? Email = null, string? Address = null, DateTimeOffset CreatedAt = default, DateTimeOffset? UpdatedAt = null);
