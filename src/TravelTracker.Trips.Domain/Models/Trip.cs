namespace TravelTracker.Trips.Domain.Models;

/**
 * A dated itinerary that may be empty, contain one stop or follow connected legs.
 * Immutable: an edit returns a new trip or throws,
 * so a rejected change leaves the existing trip untouched. Nothing is ever
 * corrected silently; every problem is reported against its field.
 * Stays and visits are deferred (roadmap concepts in docs/domain.md).
 */
public sealed class Trip {
    /** Public identifier, assigned when the trip is first created and kept when a stored trip is rebuilt. */
    public Guid TripId { get; }
    /** Stored revision, starting at one and advancing with each committed replacement. */
    public long Revision { get; }
    /** Trip name. */
    public string Name { get; }
    /** Zone the traveller enters times in. */
    public TimeZoneInfo TimeZone { get; }
    /** Trip start; no leg may depart earlier. */
    public DateTimeOffset Start { get; }
    /** Trip end; no leg may arrive later. */
    public DateTimeOffset End { get; }
    /** The traveller's round-trip intent; it does not require a completed return journey. */
    public bool IsRoundTrip { get; }
    /** Ordered legs. */
    public IReadOnlyList<TripLeg> Legs { get; }
    /** Ordered place occurrences, including a standalone stop when no journey is planned. */
    public IReadOnlyList<TripPlace> Stops { get; }

    private Trip(Guid tripId, string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip,
        IReadOnlyList<TripLeg> legs, long revision, IReadOnlyList<TripPlace> stops) {
        TripId = tripId;
        Revision = revision;
        Name = name;
        TimeZone = timeZone;
        Start = start;
        End = end;
        IsRoundTrip = isRoundTrip;
        Legs = legs;
        Stops = stops;
    }

    /**
     * Builds a trip, or throws with every rule it breaks.
     *
     * @param name Trip name, required.
     * @param timeZone Zone the traveller enters times in.
     * @param start Trip start.
     * @param end Trip end, not before start.
     * @param isRoundTrip The traveller's round-trip intent, without imposing endpoint requirements.
     * @param legs Ordered connected legs, or an empty collection.
     * @param tripId Existing identifier when rebuilding a stored trip; a new version 7 identifier is assigned when omitted.
     * @param revision The persisted revision, or one for a new trip.
     * @param stops Optional ordered place snapshots. Without legs, zero or one stop is supported. With legs, they must match the leg endpoints; omitted stops are derived from the legs.
     * @return The valid trip.
     * @throws TripValidationException when any rule is broken.
     */
    public static Trip Create(string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, IEnumerable<TripLeg> legs,
        Guid? tripId = null, long revision = 1, IEnumerable<TripPlace>? stops = null) {
        var snapshot = legs?.ToList() ?? [];
        var derivedStops = DeriveStops(snapshot);
        var stopSnapshot = stops?.ToList() ?? derivedStops;
        var errors = Validate(name, timeZone, start, end, snapshot, stopSnapshot);
        if (legs is null) errors.Add(new("legs", "legs must be a collection, which may be empty."));
        if (snapshot.Count > 0 && !stopSnapshot.SequenceEqual(derivedStops))
            errors.Add(new("stops", "stops must match the ordered leg endpoints."));
        return errors.Count > 0
            ? throw new TripValidationException(errors)
            : new(tripId ?? Guid.CreateVersion7(), name, timeZone, start, end, isRoundTrip,
                snapshot.AsReadOnly(), revision, stopSnapshot.AsReadOnly());
    }

    /**
     * Replaces all legs and derives their stop occurrences, keeping the trip basics.
     *
     * @param legs The new ordered legs.
     * @return A new valid trip; this trip is never modified.
     * @throws TripValidationException when the new legs break a rule.
     */
    public Trip WithLegs(IEnumerable<TripLeg> legs) => Create(Name, TimeZone, Start, End, IsRoundTrip, legs, TripId, Revision);

    private static List<TripError> Validate(string name, TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end,
        List<TripLeg> legs, List<TripPlace> stops) {
        var errors = new List<TripError>();
        void Fail(string field, string message) => errors.Add(new(field, message));
        if (string.IsNullOrWhiteSpace(name)) Fail("name", "name is required.");
        if (end < start) Fail("end", "trip end must not be before trip start.");
        if (zone is null) Fail("timeZone", "a trip time zone is required.");
        if (legs.Count == 0 && stops.Count > 1)
            Fail("stops", "multiple stops require connected legs.");
        for (var i = 0; i < stops.Count; i++) CheckPlace(stops[i], $"stops[{i}]", Fail);
        for (var i = 0; i < legs.Count; i++) {
            var leg = legs[i];
            var path = $"legs[{i}]";
            if (leg is null) { Fail(path, "a leg is required."); continue; }
            if (leg.Sequence != i + 1) Fail($"{path}.sequence", $"expected sequence {i + 1} but got {leg.Sequence}; legs must be ordered without gaps.");
            CheckPlace(leg.From, $"{path}.from", Fail);
            CheckPlace(leg.To, $"{path}.to", Fail);
            if (leg.Transport is null || leg.Transport.TransportModeId == Guid.Empty) Fail($"{path}.transport", "a transport mode is required.");
            if (leg.Arrival <= leg.Departure) Fail($"{path}.arrival", "arrival must be after departure.");
            if (leg.Departure < start) Fail($"{path}.departure", "departure is before the trip start.");
            if (leg.Arrival > end) Fail($"{path}.arrival", "arrival is after the trip end.");
            if (zone is not null) {
                CheckZoneOffset(leg.Departure, zone, $"{path}.departure", Fail);
                CheckZoneOffset(leg.Arrival, zone, $"{path}.arrival", Fail);
            }
            if (i == 0) continue;
            var previous = legs[i - 1];
            if (previous is null) continue;
            if (leg.From is not null && previous.To is not null &&
                (leg.From.PlaceId != previous.To.PlaceId || leg.From.Type != previous.To.Type))
                Fail($"{path}.from", "must start where the previous leg ended.");
            if (leg.Departure < previous.Arrival) Fail($"{path}.departure", "overlaps the previous leg; depart at or after its arrival.");
        }
        return errors;
    }

    /**
     * Derives each place occurrence without sorting or changing the submitted itinerary.
     * @param legs The submitted leg snapshots.
     * @return The first departure followed by each arrival, or an empty list when there are no legs.
     */
    private static List<TripPlace> DeriveStops(List<TripLeg> legs) => legs.Count == 0 ? []
        : legs.Where(leg => leg is not null).SelectMany((leg, index) => index == 0
            ? new[] { leg.From, leg.To } : [leg.To]).ToList();

    /**
     * Checks a saved location snapshot independently of its user-selected category.
     * @param place The location supplied by its owning catalogue.
     * @param field The path used to report invalid fields.
     * @param fail Appends a field error.
     */
    private static void CheckPlace(TripPlace? place, string field, Action<string, string> fail) {
        if (place is null) { fail(field, "a place is required."); return; }
        if (place.PlaceId == Guid.Empty) fail($"{field}.placeId", "a public place identifier is required.");
        if (place.Type is not ("accommodation" or "attraction" or "transport_stop"))
            fail($"{field}.type", "place type must be accommodation, attraction or transport_stop.");
        if (string.IsNullOrWhiteSpace(place.Name)) fail($"{field}.name", "place name is required.");
        if (place.Address is null) fail($"{field}.address", "place address must not be null.");
        if (place.Latitude is < -90m or > 90m) fail($"{field}.lat", "latitude must be between -90 and 90.");
        if (place.Longitude is < -180m or > 180m) fail($"{field}.lon", "longitude must be between -180 and 180.");
    }

    // a time whose offset disagrees with the trip zone was entered wrongly; report it rather than fix it
    private static void CheckZoneOffset(DateTimeOffset time, TimeZoneInfo zone, string field, Action<string, string> fail) {
        var local = time.DateTime;
        var valid = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Contains(time.Offset) : !zone.IsInvalidTime(local) && zone.GetUtcOffset(local) == time.Offset;
        if (!valid) fail(field, $"{local:s}{time.Offset} is not a valid time in {zone.Id}; use TripTime.Resolve.");
    }
}
