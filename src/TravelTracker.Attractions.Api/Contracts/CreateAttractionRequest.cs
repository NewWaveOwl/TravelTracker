namespace TravelTracker.Attractions.Api.Contracts;

/**
 * Contains writable create fields; missing required numbers stay distinguishable from zero.
 */
public sealed class CreateAttractionRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Rating { get; init; }
    public string? Address { get; init; } = string.Empty;
    public decimal? Lat { get; init; }
    public decimal? Lon { get; init; }
    public int? EstimatedVisitMinutes { get; init; }
}
