namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains writable fields while retaining missing required values.
 */
public sealed class CreateTransportRouteRequest
{
    public string? Name { get; init; }
    public Guid? TransportModeId { get; init; }
    public Guid? StartPointId { get; init; }
    public Guid? DestinationPointId { get; init; }
    public DateTimeOffset? DepartureAt { get; init; }
    public DateTimeOffset? ArrivalAt { get; init; }
    public int? BoardingMinutes { get; init; }
    public IReadOnlyList<CreateTransportStopRequest?>? Stops { get; init; } = [];
}
