namespace TravelTracker.Api.Trips;

/**
 * A requested place, identified by its public id in the service that owns it.
 *
 * @param PlaceId Public identifier of the place.
 * @param Type accommodation, attraction or transport_stop; selects the service to ask.
 */
public sealed record TripPlaceReference(Guid PlaceId, string Type);
