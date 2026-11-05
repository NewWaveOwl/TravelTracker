namespace TravelTracker.Users.Data.Entities;

/** Service-owned durable account row; provider identity is never a public HTTP identifier. */
internal sealed class UserRow
{
    public Guid UserId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "traveller";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Address { get; set; }
}
