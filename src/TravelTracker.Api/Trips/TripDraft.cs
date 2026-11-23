using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * A trip as the traveller submitted it, before references are resolved and snapshots are taken.
 *
 * @param Name Trip name.
 * @param TimeZone Zone the traveller entered times in.
 * @param Start Trip start.
 * @param End Trip end.
 * @param IsRoundTrip The traveller's round-trip intent, without requiring a return leg.
 * @param Legs Requested ordered legs.
 * @param Stops Optional ordered place references; null derives them from legs.
 */
public sealed record TripDraft(string Name, TimeZoneInfo TimeZone, DateTimeOffset Start, DateTimeOffset End, bool IsRoundTrip,
    IReadOnlyList<TripLegDraft> Legs, IReadOnlyList<TripPlaceReference>? Stops = null);
