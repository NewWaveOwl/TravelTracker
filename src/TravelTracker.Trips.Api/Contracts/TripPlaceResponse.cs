namespace TravelTracker.Trips.Api.Contracts;

/**
 * public snapshot of one place as it was when the trip was saved.
 *
 * @param PlaceId public identifier of the place in its owning service.
 * @param Type accommodation, attraction or transport_stop.
 * @param Name place name at save time.
 * @param Address place address at save time.
 * @param Lat latitude at save time.
 * @param Lon longitude at save time.
 */
public sealed record TripPlaceResponse(Guid PlaceId, string Type, string Name, string Address, decimal Lat, decimal Lon);
