namespace TravelTracker.Frontend.Api.Accommodations;

/** Caller-writable fields only; the service assigns the public ID and owner. */
public sealed record CreateAccommodationRequest(string Name, string Address, decimal Lat, decimal Lon, string Visibility);
