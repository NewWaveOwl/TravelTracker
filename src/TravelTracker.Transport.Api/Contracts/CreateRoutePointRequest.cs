namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains writable fields while retaining missing required values.
 */
public sealed class CreateRoutePointRequest
{
    public string? Type { get; init; }
    public string? Name { get; init; }
    public string? Address { get; init; } = string.Empty;
    public decimal? Lat { get; init; }
    public decimal? Lon { get; init; }
    public Guid? SourceId { get; init; }
}
