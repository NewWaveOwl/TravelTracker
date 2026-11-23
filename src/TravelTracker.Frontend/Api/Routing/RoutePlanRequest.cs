namespace TravelTracker.Frontend.Api.Routing;

/** Chosen endpoints and an explicit departure instant; previewing never changes trip times. */
public sealed record RoutePlanRequest(RoutePoint Origin, RoutePoint Destination, string Mode, DateTimeOffset DepartureTime, string? TimeZone = null);
