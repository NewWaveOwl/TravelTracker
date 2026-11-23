namespace TravelTracker.Frontend.Api.Routing;

/** A suggested address at provider coordinates; selecting it does not save a place. */
public sealed record AddressCandidate(string Label, decimal Lat, decimal Lon, string Source);
