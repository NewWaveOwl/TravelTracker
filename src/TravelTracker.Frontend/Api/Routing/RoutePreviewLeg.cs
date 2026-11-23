namespace TravelTracker.Frontend.Api.Routing;

/** Geometry and timing evidence for one provider-calculated route section. */
public sealed record RoutePreviewLeg(string Mode, double DurationSeconds, double DistanceMeters,
    DateTimeOffset DepartureTime, DateTimeOffset ArrivalTime, string Geometry, string? RouteName, bool Realtime);
