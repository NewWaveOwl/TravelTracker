namespace TravelTracker.Frontend.Api.Routing;

/** A mode is available, unsupported or honestly unknown according to the loaded router. */
public sealed record RoutingModeCapability(string Code, string Status, string? Reason = null, string? Basis = null);
