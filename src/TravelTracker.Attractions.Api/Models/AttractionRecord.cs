namespace TravelTracker.Attractions.Api.Models;

/**
 * Stores an attraction without navigation properties or update machinery.
 * @param AttractionId Generated identifier.
 * @param Name Attraction name.
 * @param Description Optional description.
 * @param Rating Optional manually entered rating.
 * @param Address Attraction address.
 * @param Lat Latitude.
 * @param Lon Longitude.
 * @param EstimatedVisitMinutes Typical visit duration.
 */
public sealed record AttractionRecord(Guid AttractionId, string Name, string? Description,
    decimal? Rating, string Address, decimal Lat, decimal Lon, int EstimatedVisitMinutes);
