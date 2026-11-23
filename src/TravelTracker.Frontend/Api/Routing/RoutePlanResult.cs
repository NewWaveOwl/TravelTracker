namespace TravelTracker.Frontend.Api.Routing;

/** Route candidates, or an explicit no-route or unsupported outcome. */
public sealed record RoutePlanResult(string Status, IReadOnlyList<RouteItinerary> Itineraries,
    RoutingCoverage? Coverage = null, string? Reason = null, string? TimeZone = null, DateTimeOffset? RequestedDepartureTime = null);
