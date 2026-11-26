using TravelTracker.Users.Domain.Types;

namespace TravelTracker.Users.Data.Entities;

/**
 * Stores account and profile fields with persistence-only identity and concurrency state.
 */
internal sealed class UserRecord
{
    public Id64 Id { get; set; }
    public Guid PublicId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Role { get; set; } = "traveller";
    public string Status { get; set; } = "active";
    public string? Username { get; set; }
    /** Case-folded username, the sign-in name; fixed at registration. */
    public string? NormalizedUsername { get; set; }
    public string? Address { get; set; }
    public UserPictureRow? Picture { get; set; }
    public string? PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
