namespace TravelTracker.Trips.Api.Contracts;

/**
 * public representation of one saved trip. Owner identity and storage keys are never included.
 *
 * @param TripId public trip identifier.
 * @param Name trip name.
 * @param TimeZone time zone id the traveller entered times in.
 * @param Start trip start with its offset.
 * @param End trip end with its offset.
 * @param IsRoundTrip whether the trip returns to its starting accommodation.
 * @param Legs ordered legs.
 */
public sealed record TripResponse(Guid TripId, string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End,
    bool IsRoundTrip, IReadOnlyList<TripLegResponse> Legs);
