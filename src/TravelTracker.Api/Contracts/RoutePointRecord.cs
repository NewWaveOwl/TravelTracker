namespace TravelTracker.Api.Contracts;

/**
 * Represents the agreed location HTTP response mapped from the shared Domain point.
 * @param RoutePointId Generated identifier.
 * @param Type Accommodation, attraction or transport_stop.
 * @param Name Location name.
 * @param Address Location address.
 * @param Lat Latitude.
 * @param Lon Longitude.
 * @param SourceId Optional metadata referring to another catalogue entry.
 */
public sealed record RoutePointRecord(Guid RoutePointId, string Type, string Name,
    string Address, decimal Lat, decimal Lon, Guid? SourceId);
