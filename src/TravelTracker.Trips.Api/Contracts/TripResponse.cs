namespace TravelTracker.Trips.Api.Contracts;

/**
 * public representation of one saved trip. Owner identity and storage keys are never included.
 *
 * @param TripId Public trip identifier.
 * @param Name Trip name.
 * @param TimeZone Time zone id the traveller entered times in.
 * @param Start Trip start with its offset.
 * @param End Trip end with its offset.
 * @param IsRoundTrip The traveller's round-trip intent, without requiring a return leg.
 * @param Legs Ordered legs.
 * @param Stops Ordered place snapshots, including a lone stop with no journey.
 */
public sealed record TripResponse(Guid TripId, string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End,
    bool IsRoundTrip, IReadOnlyList<TripLegResponse> Legs, IReadOnlyList<TripPlaceResponse> Stops);
