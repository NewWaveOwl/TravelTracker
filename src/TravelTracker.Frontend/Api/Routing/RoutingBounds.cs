namespace TravelTracker.Frontend.Api.Routing;

/** Optional geographic bounds reported by the router for its loaded data. */
public sealed record RoutingBounds(decimal MinLat, decimal MinLon, decimal MaxLat, decimal MaxLon);
