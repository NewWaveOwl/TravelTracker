namespace TravelTracker.Frontend.Api.Routing;

/** Live router metadata describes supported modes and loaded schedules, not a guarantee of a route. */
public sealed record RoutingCapabilities(string Status, RoutingRouter Router, RoutingCoverage Coverage,
    IReadOnlyList<RoutingModeCapability> Modes, string Attribution);
