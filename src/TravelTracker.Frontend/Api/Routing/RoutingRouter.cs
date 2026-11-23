namespace TravelTracker.Frontend.Api.Routing;

/** Identity of the running shared router, with its version when available. */
public sealed record RoutingRouter(string Name, string? Version);
