namespace TravelTracker.Frontend.Api.Routing;

/** A provider-labelled route endpoint or intermediate transit stop. */
public sealed record RouteStop(string? Name, decimal Lat, decimal Lon, string? StopId = null);
