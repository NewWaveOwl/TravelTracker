namespace TravelTracker.Transport.Data.Entities;

/** Stores Transport-owned values without exposing internal keys in HTTP. */
public sealed class TransportModeRow
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
}
