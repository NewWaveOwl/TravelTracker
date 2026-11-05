namespace TravelTracker.Frontend.Api.Transport;

/** Saved reusable location public HTTP representation. */
public sealed record RoutePointDto(Guid RoutePointId, string Type, string Name, string Address, decimal Lat, decimal Lon, Guid? SourceId);
