namespace TravelTracker.Trips.Domain.Models;

/**
 * One ordered journey inside a trip, with snapshots of its two places and its transport mode.
 * Times are absolute (offset included) so they are never ambiguous; use
 * TripTime.Resolve to turn a local time into one.
 *
 * @param Sequence One-based position in the trip, without gaps.
 * @param From Where the journey starts.
 * @param To Where the journey ends.
 * @param Transport The transport mode used for the journey.
 * @param Departure When the journey leaves From.
 * @param Arrival When the journey reaches To; must be after Departure.
 */
public sealed record TripLeg(int Sequence, TripPlace From, TripPlace To, TripTransport Transport,
    DateTimeOffset Departure, DateTimeOffset Arrival);
