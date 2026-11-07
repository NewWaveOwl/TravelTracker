namespace TravelTracker.Trips.Api.Contracts;

/**
 * Public snapshot of one place as it was when the trip was saved.
 *
 * @param PlaceId Public identifier of the place in its owning service.
 * @param Type accommodation, attraction or transport_stop.
 * @param Name Place name at save time.
 * @param Address Place address at save time.
 * @param Lat Latitude at save time.
 * @param Lon Longitude at save time.
 */
public sealed record TripPlaceResponse(Guid PlaceId, string Type, string Name, string Address, decimal Lat, decimal Lon);
