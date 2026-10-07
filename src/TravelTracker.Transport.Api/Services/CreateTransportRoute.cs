using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Validation;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class TransportRouteService
{
    /**
     * Validates the schedule and resolves every reference before saving its immutable snapshots.
     * @param request Scheduled route fields, including ordered stop timings.
     * @param cancellationToken Cancels lookups and insertion.
     * @return The complete saved route; failures never insert a partial route.
     */
    public async Task<TransportRouteRecord> CreateAsync(CreateTransportRouteRequest request, CancellationToken cancellationToken)
    {
        TransportRouteValidator.Validate(request);
        var transport = await transports.GetByIdAsync(request.TransportModeId!.Value, cancellationToken)
            ?? throw new ResourceNotFoundException("transport");
        var start = await points.GetByIdAsync(request.StartPointId!.Value, cancellationToken)
            ?? throw new ResourceNotFoundException("start point");
        var destination = await points.GetByIdAsync(request.DestinationPointId!.Value, cancellationToken)
            ?? throw new ResourceNotFoundException("destination point");
        var stops = new List<TransportStopRecord>();
        foreach (var requested in request.Stops!)
        {
            var stop = requested!;
            var point = await points.GetByIdAsync(stop.PointId!.Value, cancellationToken)
                ?? throw new ResourceNotFoundException("stop point");
            stops.Add(new(point, stop.ArrivalAt!.Value.ToUniversalTime(),
                stop.DepartureAt!.Value.ToUniversalTime(), stop.BoardingMinutes!.Value,
                stop.CanLeaveVehicle!.Value));
        }
        var value = new TransportRouteRecord(Guid.NewGuid(), request.Name!.Trim(), transport, start, destination,
            request.DepartureAt!.Value.ToUniversalTime(), request.ArrivalAt!.Value.ToUniversalTime(),
            request.BoardingMinutes!.Value, stops.AsReadOnly());
        await repository.AddAsync(value, cancellationToken);
        return value;
    }
}
