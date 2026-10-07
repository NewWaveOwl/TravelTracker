namespace TravelTracker.Domain.Models;

/**
 * Stores a reusable location independently of a journey's schedule.
 * @param RoutePointId Generated location identifier.
 * @param Type Accommodation, attraction or transport_stop.
 * @param Name Location name.
 * @param Address Location address.
 * @param Latitude Location latitude.
 * @param Longitude Location longitude.
 * @param SourceId Optional unverified place-catalogue metadata.
 */
public sealed record RoutePoint(Guid RoutePointId, string Type, string Name, string Address,
    decimal Latitude, decimal Longitude, Guid? SourceId);
