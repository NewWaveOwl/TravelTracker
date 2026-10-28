namespace TravelTracker.Domain.Models;

/**
 * One ordered journey inside a trip, with snapshots of its two route points.
 * Times are absolute (offset included) so they are never ambiguous; use
 * TripTime.Resolve to turn a local time into one.
 *
 * @param Sequence One-based position in the trip, without gaps.
 * @param From Where the journey starts.
 * @param To Where the journey ends.
 * @param TransportCode car, bus, train, pedestrian, bike or plane.
 * @param Departure When the journey leaves From.
 * @param Arrival When the journey reaches To; must be after Departure.
 */
public sealed record TripLeg(int Sequence, RoutePoint From, RoutePoint To, string TransportCode,
    DateTimeOffset Departure, DateTimeOffset Arrival);
