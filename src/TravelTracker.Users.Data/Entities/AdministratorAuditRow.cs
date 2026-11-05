namespace TravelTracker.Users.Data.Entities;

/** Durable administrator-creation audit; no credentials or bootstrap secret are stored. */
internal sealed class AdministratorAuditRow
{
    public Guid AuditId { get; set; }
    public Guid UserId { get; set; }
    public string Reason { get; set; } = "";
    public Guid? ActorUserId { get; set; }
    public string? PreviousRole { get; set; }
    public string? NewRole { get; set; }
    public string Action { get; set; } = "bootstrap";
    public DateTimeOffset RecordedAt { get; set; }
}
