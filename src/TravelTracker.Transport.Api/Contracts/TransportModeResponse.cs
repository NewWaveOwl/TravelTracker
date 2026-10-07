namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains the public TransportMode representation.
 * @param TransportModeId The transport mode id value.
 * @param Code The code value.
 * @param Name The name value.
 * @param Description The description value.
 */
public sealed record TransportModeResponse(Guid TransportModeId, string Code, string Name, string? Description);
