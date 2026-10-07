namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Represents the public attraction fields, including lat/lon coordinate names.
 * @param AttractionId The attraction id value.
 * @param Name The name value.
 * @param Description The description value.
 * @param Rating The rating value.
 * @param Address The address value.
 * @param Lat The lat value.
 * @param Lon The lon value.
 * @param EstimatedVisitMinutes The estimated visit minutes value.
 */
public sealed record AttractionResponse(
    Guid AttractionId, string Name, string? Description, decimal? Rating,
    string Address, decimal Lat, decimal Lon, int EstimatedVisitMinutes);
