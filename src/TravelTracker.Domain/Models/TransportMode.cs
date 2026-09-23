namespace TravelTracker.Domain.Models;

public sealed class TransportMode
{
    public Guid TransportModeId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}
