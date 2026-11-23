namespace TravelTracker.Frontend.Api.Routing;

/** Geometry and timing evidence for one provider-calculated route section. */
public sealed record RoutePreviewLeg(string Mode, double DurationSeconds, double DistanceMeters,
    DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime, string Geometry, string? RouteName, bool Realtime,
    RouteStop? From = null, RouteStop? To = null, IReadOnlyList<RouteStop>? IntermediateStops = null,
    string? RouteId = null, string? Headsign = null, string? TimingBasis = null,
    DateTimeOffset? ScheduledDepartureTime = null, DateTimeOffset? ScheduledArrivalTime = null);
