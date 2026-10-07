namespace TravelTracker.Transport.Api.Models;
/**
 * Represents one scheduled intermediate stop and its preparation time.
 * @param Point The point value.
 * @param ArrivalAt The arrival at value.
 * @param DepartureAt The departure at value.
 * @param BoardingMinutes The boarding minutes value.
 * @param CanLeaveVehicle The can leave vehicle value.
 */
public sealed record TransportStopRecord(RoutePointRecord Point, DateTimeOffset ArrivalAt, DateTimeOffset DepartureAt, int BoardingMinutes, bool CanLeaveVehicle);
