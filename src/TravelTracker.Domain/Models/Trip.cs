namespace TravelTracker.Domain.Models;

/**
 * A dated, connected itinerary. Immutable: an edit returns a new trip or throws,
 * so a rejected change leaves the existing trip untouched. Nothing is ever
 * corrected silently; every problem is reported against its field.
 * Stays and visits are deferred (roadmap concepts in docs/domain.md).
 */
public sealed class Trip {
    private static readonly HashSet<string> TransportCodes = ["car", "bus", "train", "pedestrian", "bike", "plane"];

    /** Trip name. */
    public string Name { get; }
    /** Zone the traveller enters times in. */
    public TimeZoneInfo TimeZone { get; }
    /** Trip start; no leg may depart earlier. */
    public DateTimeOffset Start { get; }
    /** Trip end; no leg may arrive later. */
    public DateTimeOffset End { get; }
    /** True when the last leg must return to where the first one started. */
    public bool IsRoundTrip { get; }
    /** Ordered legs. */
    public IReadOnlyList<TripLeg> Legs { get; }

    private Trip(string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, IReadOnlyList<TripLeg> legs) {
        Name = name;
        TimeZone = timeZone;
        Start = start;
        End = end;
        IsRoundTrip = isRoundTrip;
        Legs = legs;
    }

    /**
     * Builds a trip, or throws with every rule it breaks.
     *
     * @param name Trip name, required.
     * @param timeZone Zone the traveller enters times in.
     * @param start Trip start.
     * @param end Trip end, not before start.
     * @param isRoundTrip Whether the trip must return to its starting point.
     * @param legs Ordered legs; at least one.
     * @return The valid trip.
     * @throws TripValidationException when any rule is broken.
     */
    public static Trip Create(string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, IEnumerable<TripLeg> legs) {
        var snapshot = legs.ToList();
        var errors = Validate(name, timeZone, start, end, isRoundTrip, snapshot);
        return errors.Count > 0 ? throw new TripValidationException(errors) : new(name, timeZone, start, end, isRoundTrip, snapshot);
    }

    /**
     * Replaces all legs, keeping the other trip fields.
     *
     * @param legs The new ordered legs.
     * @return A new valid trip; this trip is never modified.
     * @throws TripValidationException when the new legs break a rule.
     */
    public Trip WithLegs(IEnumerable<TripLeg> legs) => Create(Name, TimeZone, Start, End, IsRoundTrip, legs);

    private static List<TripError> Validate(string name, TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, List<TripLeg> legs) {
        var errors = new List<TripError>();
        void Fail(string field, string message) => errors.Add(new(field, message));
        if (string.IsNullOrWhiteSpace(name)) Fail("name", "name is required.");
        if (end < start) Fail("end", "trip end must not be before trip start.");
        if (legs.Count == 0) { Fail("legs", "a trip needs at least one leg."); return errors; }
        for (var i = 0; i < legs.Count; i++) {
            var leg = legs[i];
            var path = $"legs[{i}]";
            if (leg.Sequence != i + 1) Fail($"{path}.sequence", $"expected sequence {i + 1} but got {leg.Sequence}; legs must be ordered without gaps.");
            if (!TransportCodes.Contains(leg.TransportCode)) Fail($"{path}.transportCode", $"'{leg.TransportCode}' is not one of {string.Join(", ", TransportCodes)}.");
            if (leg.Arrival <= leg.Departure) Fail($"{path}.arrival", "arrival must be after departure.");
            if (leg.Departure < start) Fail($"{path}.departure", "departure is before the trip start.");
            if (leg.Arrival > end) Fail($"{path}.arrival", "arrival is after the trip end.");
            CheckZoneOffset(leg.Departure, zone, $"{path}.departure", Fail);
            CheckZoneOffset(leg.Arrival, zone, $"{path}.arrival", Fail);
            if (i == 0) continue;
            var previous = legs[i - 1];
            if (leg.From.RoutePointId != previous.To.RoutePointId) Fail($"{path}.from", "must start where the previous leg ended.");
            if (leg.Departure < previous.Arrival) Fail($"{path}.departure", "overlaps the previous leg; depart at or after its arrival.");
        }
        var first = legs[0];
        var last = legs[^1];
        if (first.From.Type != "accommodation") Fail("legs[0].from", "a trip must start at an accommodation.");
        if (isRoundTrip) {
            if (last.To.RoutePointId != first.From.RoutePointId) Fail($"legs[{legs.Count - 1}].to", "a round trip must return to where it started.");
            if (!legs.Any(leg => leg.To.Type == "attraction")) Fail("legs", "a round trip must include at least one attraction.");
        } else if (last.To.Type != "attraction") Fail($"legs[{legs.Count - 1}].to", "a one-way trip must end at an attraction.");
        return errors;
    }

    // a time whose offset disagrees with the trip zone was entered wrongly; report it rather than fix it
    private static void CheckZoneOffset(DateTimeOffset time, TimeZoneInfo zone, string field, Action<string, string> fail) {
        var local = time.DateTime;
        var valid = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Contains(time.Offset) : !zone.IsInvalidTime(local) && zone.GetUtcOffset(local) == time.Offset;
        if (!valid) fail(field, $"{local:s}{time.Offset} is not a valid time in {zone.Id}; use TripTime.Resolve.");
    }
}
