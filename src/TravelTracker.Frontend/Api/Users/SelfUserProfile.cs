namespace TravelTracker.Frontend.Api.Users;

/**
 * The signed-in caller's public profile returned by the Users API.
 * @param PublicId Public identifier for the caller's local TravelTracker account.
 * @param Role Server-owned application role.
 * @param Status Server-owned account status.
 * @param Username Sign-in name chosen at registration; it cannot be changed.
 * @param Address Caller-editable optional address.
 * @param CreatedAt Account creation time.
 * @param UpdatedAt Most recent profile update time, when one exists.
 */
public sealed record SelfUserProfile(Guid PublicId, string Role, string Status, string? Username = null, string? Address = null, DateTimeOffset CreatedAt = default, DateTimeOffset? UpdatedAt = null);
