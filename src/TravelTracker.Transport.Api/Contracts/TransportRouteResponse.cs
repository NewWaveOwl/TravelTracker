namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains the public TransportRoute representation.
 * @param TransportRouteId The transport route id value.
 * @param Name The name value.
 * @param Transport The transport value.
 * @param Start The start value.
 * @param Destination The destination value.
 * @param DepartureAt The departure at value.
 * @param ArrivalAt The arrival at value.
 * @param BoardingMinutes The boarding minutes value.
 * @param BoardingStartsAt The boarding starts at value.
 * @param ElapsedMinutes The elapsed minutes value.
 * @param TotalMinutes The total minutes value.
 * @param Stops The stops value.
 */
public sealed record TransportRouteResponse(Guid TransportRouteId, string Name, TransportModeResponse Transport, RoutePointResponse Start, RoutePointResponse Destination, DateTimeOffset DepartureAt, DateTimeOffset ArrivalAt, int BoardingMinutes, DateTimeOffset BoardingStartsAt, double ElapsedMinutes, double TotalMinutes, IReadOnlyList<TransportStopResponse> Stops);
