namespace TravelTracker.Domain.Models;

/**
 * Represents one transportation method that can be assigned to a trip leg.
 */
public sealed class TransportMode
{
    /**
     * Gets or sets the unique identifier of the transportation method.
     */
    public Guid TransportModeId { get; set; }

    /**
     * Gets or sets the stable machine-readable code, such as car or train.
     */
    public string Code { get; set; } = string.Empty;

    /**
     * Gets or sets the human-readable transportation method name.
     */
    public string DisplayName { get; set; } = string.Empty;
}
