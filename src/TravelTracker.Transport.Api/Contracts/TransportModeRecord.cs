namespace TravelTracker.Transport.Api.Contracts;

/**
 * Represents the agreed transport HTTP response; Name maps the shared DisplayName.
 * @param TransportModeId Generated public identifier.
 * @param Code Stable machine-readable mode, such as car or train.
 * @param Name Human-readable mode name.
 * @param Description Optional free-text description.
 */
public sealed record TransportModeRecord(Guid TransportModeId, string Code, string Name, string? Description);
