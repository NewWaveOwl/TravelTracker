using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Trips;

/**
 * A trip as the traveller submitted it, before references are resolved and snapshots are taken.
 *
 * @param Name Trip name.
 * @param TimeZone Zone the traveller entered times in.
 * @param Start Trip start.
 * @param End Trip end.
 * @param IsRoundTrip Whether the trip must return to its starting place.
 * @param Legs Requested ordered legs.
 */
public sealed record TripDraft(string Name, TimeZoneInfo TimeZone, DateTimeOffset Start, DateTimeOffset End, bool IsRoundTrip,
    IReadOnlyList<TripLegDraft> Legs);
