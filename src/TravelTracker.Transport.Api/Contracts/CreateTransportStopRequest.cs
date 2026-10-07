namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains writable fields while retaining missing required values.
 */
public sealed class CreateTransportStopRequest
{
    public Guid? PointId { get; init; }
    public DateTimeOffset? ArrivalAt { get; init; }
    public DateTimeOffset? DepartureAt { get; init; }
    public int? BoardingMinutes { get; init; }
    public bool? CanLeaveVehicle { get; init; }
}
