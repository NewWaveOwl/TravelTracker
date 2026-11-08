namespace TravelTracker.Frontend.Api.Accommodations;

/** Public accommodation response without owner or persistence details. */
public sealed record AccommodationDto(Guid AccommodationId, string Name, string Address,
    decimal Lat, decimal Lon, string Visibility);
