namespace TravelTracker.Trips.Api.Contracts;

/**
 * Public representation of one saved trip. Owner identity and storage keys are never included.
 *
 * @param TripId Public trip identifier.
 * @param Name Trip name.
 * @param TimeZone Time zone id the traveller entered times in.
 * @param Start Trip start with its offset.
 * @param End Trip end with its offset.
 * @param IsRoundTrip Whether the trip returns to its starting accommodation.
 * @param Legs Ordered legs.
 */
public sealed record TripResponse(Guid TripId, string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End,
    bool IsRoundTrip, IReadOnlyList<TripLegResponse> Legs);
