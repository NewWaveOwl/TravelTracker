using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Contracts;
/**
 * Projects a validated schedule and derives its timing information.
 */
public static class TransportRouteResponseMapper
{
    /**
     * Derives preparation and potential free windows from supplied instants.
     * @param value The saved scheduled route.
     * @return Public snapshots and durations; potential windows are not attraction suggestions.
     */
    public static TransportRouteResponse From(TransportRouteRecord value)
    {
        var elapsed = (value.ArrivalAt - value.DepartureAt).TotalMinutes;
        var stops = value.Stops.Select(stop =>
        {
            var downtime = (stop.DepartureAt - stop.ArrivalAt).TotalMinutes;
            return new TransportStopResponse(RoutePointResponseMapper.From(stop.Point),
                stop.ArrivalAt, stop.DepartureAt, stop.BoardingMinutes,
                stop.DepartureAt.AddMinutes(-stop.BoardingMinutes), downtime,
                stop.CanLeaveVehicle, stop.CanLeaveVehicle ? downtime - stop.BoardingMinutes : 0);
        }).ToArray();
        return new(value.TransportRouteId, value.Name, TransportModeResponseMapper.From(value.Transport),
            RoutePointResponseMapper.From(value.Start), RoutePointResponseMapper.From(value.Destination),
            value.DepartureAt, value.ArrivalAt, value.BoardingMinutes,
            value.DepartureAt.AddMinutes(-value.BoardingMinutes), elapsed,
            elapsed + value.BoardingMinutes, Array.AsReadOnly(stops));
    }
}
