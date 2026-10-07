namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains the public RoutePoint representation.
 * @param RoutePointId The route point id value.
 * @param Type The type value.
 * @param Name The name value.
 * @param Address The address value.
 * @param Lat The lat value.
 * @param Lon The lon value.
 * @param SourceId The source id value.
 */
public sealed record RoutePointResponse(Guid RoutePointId, string Type, string Name, string Address, decimal Lat, decimal Lon, Guid? SourceId);
