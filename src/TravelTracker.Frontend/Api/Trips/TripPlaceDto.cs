namespace TravelTracker.Frontend.Api.Trips;

/** A stop from a place catalogue or a saved trip snapshot. */
public sealed record TripPlaceDto(Guid PlaceId, string Type, string Name, string Address, decimal Lat, decimal Lon);
