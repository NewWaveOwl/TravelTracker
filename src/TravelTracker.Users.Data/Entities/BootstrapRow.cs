namespace TravelTracker.Users.Data.Entities;

/** Permanent first-administrator completion marker, retained independently of later account changes. */
internal sealed class BootstrapRow
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset CompletedAt { get; set; }
}
