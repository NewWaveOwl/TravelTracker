namespace TravelTracker.Trips.Domain.Models;

/**
 * one ordered journey inside a trip, with snapshots of its two places and its transport mode.
 * times are absolute (offset included) so they are never ambiguous; use
 * TripTime.Resolve to turn a local time into one.
 *
 * @param Sequence one-based position in the trip, without gaps.
 * @param From where the journey starts.
 * @param To where the journey ends.
 * @param Transport the transport mode used for the journey.
 * @param Departure when the journey leaves From.
 * @param Arrival when the journey reaches To; must be after Departure.
 * @param EncodedPolyline Optional five-decimal encoded path of the selected route.
 */
public sealed record TripLeg(int Sequence, TripPlace From, TripPlace To, TripTransport Transport,
    DateTimeOffset Departure, DateTimeOffset Arrival, string? EncodedPolyline = null);
