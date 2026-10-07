namespace TravelTracker.Transport.Api.Models;

/**
 * Stores a reusable location with its role.
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
