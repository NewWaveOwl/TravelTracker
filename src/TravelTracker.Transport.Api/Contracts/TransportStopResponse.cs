namespace TravelTracker.Transport.Api.Contracts;
/**
 * Contains the public TransportStop representation.
 * @param Point The point value.
 * @param ArrivalAt The arrival at value.
 * @param DepartureAt The departure at value.
 * @param BoardingMinutes The boarding minutes value.
 * @param BoardingStartsAt The boarding starts at value.
 * @param DowntimeMinutes The downtime minutes value.
 * @param CanLeaveVehicle The can leave vehicle value.
 * @param PotentialFreeMinutes The potential free minutes value.
 */
public sealed record TransportStopResponse(RoutePointResponse Point, DateTimeOffset ArrivalAt, DateTimeOffset DepartureAt, int BoardingMinutes, DateTimeOffset BoardingStartsAt, double DowntimeMinutes, bool CanLeaveVehicle, double PotentialFreeMinutes);
