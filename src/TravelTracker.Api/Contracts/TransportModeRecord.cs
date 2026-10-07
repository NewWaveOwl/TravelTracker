namespace TravelTracker.Api.Contracts;
/**
 * Represents the agreed transport HTTP response; Name maps the shared DisplayName.
 * @param TransportModeId The transport mode id value.
 * @param Code The code value.
 * @param Name The name value.
 * @param Description The description value.
 */
public sealed record TransportModeRecord(Guid TransportModeId, string Code, string Name, string? Description);
