namespace TravelTracker.Domain.Models;

/**
 * Snapshot of one referenced place, copied when the trip is saved so later
 * catalogue edits never move an old trip's markers.
 *
 * @param PlaceId Public identifier of the record in its owning service.
 * @param Type accommodation, attraction or transport_stop.
 * @param Name Place name at save time.
 * @param Address Place address at save time.
 * @param Latitude Latitude at save time.
 * @param Longitude Longitude at save time.
 */
public sealed record TripPlace(Guid PlaceId, string Type, string Name, string Address, decimal Latitude, decimal Longitude);
