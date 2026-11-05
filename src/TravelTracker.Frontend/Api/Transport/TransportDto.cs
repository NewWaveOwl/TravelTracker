namespace TravelTracker.Frontend.Api.Transport;

/** Saved transport public HTTP representation; it contains no journey schedule. */
public sealed record TransportDto(Guid TransportModeId, string Code, string Name, string? Description);
