namespace TravelTracker.Frontend.Features.Trips;

/**
 * A place the traveller picked from a catalogue, kept by its public identifier.
 * @param PlaceId Public identifier in the owning service.
 * @param Type accommodation, attraction or transport_stop.
 * @param Name Display name at selection time.
 * @param Address Display address at selection time.
 */
public sealed record TripPlaceChoice(Guid PlaceId, string Type, string Name, string Address);
