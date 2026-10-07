namespace TravelTracker.Transport.Api.Models;
/**
 * Represents an immutable typed location snapshot.
 * @param RoutePointId The route point id value.
 * @param Type The type value.
 * @param Name The name value.
 * @param Address The address value.
 * @param Latitude The latitude value.
 * @param Longitude The longitude value.
 * @param SourceId The source id value.
 */
public sealed record RoutePointRecord(Guid RoutePointId, string Type, string Name, string Address, decimal Latitude, decimal Longitude, Guid? SourceId);
