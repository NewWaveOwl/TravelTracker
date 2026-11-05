namespace TravelTracker.Frontend.Api.Attractions;

/** Public attraction response; contains no persistence or internal identity types. */
public sealed record AttractionDto(Guid AttractionId, string Name, string? Description, decimal? Rating,
    string Address, decimal Lat, decimal Lon, int EstimatedVisitMinutes);
