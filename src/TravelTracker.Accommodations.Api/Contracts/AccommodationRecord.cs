namespace TravelTracker.Accommodations.Api.Contracts;

/** Public accommodation representation; excludes owner and persistence details. */
public sealed record AccommodationRecord(Guid AccommodationId, string Name, string Address,
    decimal Lat, decimal Lon, string Visibility);
