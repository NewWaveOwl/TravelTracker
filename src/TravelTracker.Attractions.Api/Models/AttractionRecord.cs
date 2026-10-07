namespace TravelTracker.Attractions.Api.Models;

/**
 * Represents an immutable attraction owned by this service.
 * @param AttractionId The attraction id value.
 * @param Name The name value.
 * @param Description The description value.
 * @param Rating The rating value.
 * @param Address The address value.
 * @param Latitude The latitude value.
 * @param Longitude The longitude value.
 * @param EstimatedVisitMinutes The estimated visit minutes value.
 */
public sealed record AttractionRecord(
    Guid AttractionId, string Name, string? Description, decimal? Rating,
    string Address, decimal Latitude, decimal Longitude, int EstimatedVisitMinutes);
