namespace TravelTracker.Transport.Api.Models;
/**
 * Represents one scheduled route with immutable transport and location snapshots.
 * @param TransportRouteId The transport route id value.
 * @param Name The name value.
 * @param Transport The transport value.
 * @param Start The start value.
 * @param Destination The destination value.
 * @param DepartureAt The departure at value.
 * @param ArrivalAt The arrival at value.
 * @param BoardingMinutes The boarding minutes value.
 * @param Stops The stops value.
 */
public sealed record TransportRouteRecord(Guid TransportRouteId, string Name, TransportModeRecord Transport, RoutePointRecord Start, RoutePointRecord Destination, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt, int BoardingMinutes, IReadOnlyList<TransportStopRecord> Stops);
