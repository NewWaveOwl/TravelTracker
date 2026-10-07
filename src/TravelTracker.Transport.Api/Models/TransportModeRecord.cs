namespace TravelTracker.Transport.Api.Models;
/**
 * Represents a named transport entry with a supported mode code.
 * @param TransportModeId The transport mode id value.
 * @param Code The code value.
 * @param Name The name value.
 * @param Description The description value.
 */
public sealed record TransportModeRecord(Guid TransportModeId, string Code, string Name, string? Description);
