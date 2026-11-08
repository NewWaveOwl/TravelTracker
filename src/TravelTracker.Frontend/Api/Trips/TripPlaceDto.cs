namespace TravelTracker.Frontend.Api.Trips;

/** The place snapshot saved on a trip leg, so the trip reads back without the catalogue services. */
public sealed record TripPlaceDto(Guid PlaceId, string Type, string Name, string Address, decimal Lat, decimal Lon);
