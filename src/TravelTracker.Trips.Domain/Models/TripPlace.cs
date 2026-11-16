namespace TravelTracker.Trips.Domain.Models;

/**
 * snapshot of one referenced place, copied when the trip is saved so later
 * catalogue edits never move an old trip's markers.
 *
 * @param PlaceId public identifier of the record in its owning service.
 * @param Type accommodation, attraction or transport_stop.
 * @param Name place name at save time.
 * @param Address place address at save time.
 * @param Latitude latitude at save time.
 * @param Longitude longitude at save time.
 */
public sealed record TripPlace(Guid PlaceId, string Type, string Name, string Address, decimal Latitude, decimal Longitude);
