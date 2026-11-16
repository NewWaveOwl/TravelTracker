namespace TravelTracker.Trips.Domain.Models;

/**
 * a dated, connected itinerary. Immutable: an edit returns a new trip or throws,
 * so a rejected change leaves the existing trip untouched. Nothing is ever
 * corrected silently; every problem is reported against its field.
 * stays and visits are deferred (roadmap concepts in docs/domain.md).
 */
public sealed class Trip
{
    /** public identifier, assigned when the trip is first created and kept when a stored trip is rebuilt. */
    public Guid TripId { get; }
    /** stored revision, starting at one and advancing with each committed replacement. */
    public long Revision { get; }
    /** trip name. */
    public string Name { get; }
    /** zone the traveller enters times in. */
    public TimeZoneInfo TimeZone { get; }
    /** trip start; no leg may depart earlier. */
    public DateTimeOffset Start { get; }
    /** trip end; no leg may arrive later. */
    public DateTimeOffset End { get; }
    /** true when the last leg must return to where the first one started. */
    public bool IsRoundTrip { get; }
    /** ordered legs. */
    public IReadOnlyList<TripLeg> Legs { get; }

    private Trip(Guid tripId, string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, IReadOnlyList<TripLeg> legs, long revision)
    {
        TripId = tripId;
        Revision = revision;
        Name = name;
        TimeZone = timeZone;
        Start = start;
        End = end;
        IsRoundTrip = isRoundTrip;
        Legs = legs;
    }

    /**
     * builds a trip, or throws with every rule it breaks.
     *
     * @param name trip name, required.
     * @param timeZone zone the traveller enters times in.
     * @param start trip start.
     * @param end trip end, not before start.
     * @param isRoundTrip whether the trip must return to its starting point.
     * @param legs ordered legs; at least one.
     * @param tripId existing identifier when rebuilding a stored trip; a new version 7 identifier is assigned when omitted.
     * @param revision the persisted revision, or one for a new trip.
     * @return the valid trip.
     * @throws TripValidationException when any rule is broken.
     */
    public static Trip Create(string name, TimeZoneInfo timeZone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, IEnumerable<TripLeg> legs,
        Guid? tripId = null, long revision = 1)
    {
        var snapshot = legs.ToList();
        var errors = Validate(name, timeZone, start, end, isRoundTrip, snapshot);
        if (tripId == Guid.Empty) errors.Add(new("tripId", "the trip id must not be empty."));
        if (revision < 1) errors.Add(new("revision", "the revision must be at least one."));
        return errors.Count > 0
            ? throw new TripValidationException(errors)
            : new(tripId ?? Guid.CreateVersion7(), name, timeZone, start, end, isRoundTrip, snapshot.AsReadOnly(), revision);
    }

    /**
     * replaces all legs, keeping the other trip fields.
     *
     * @param legs the new ordered legs.
     * @return a new valid trip; this trip is never modified.
     * @throws TripValidationException when the new legs break a rule.
     */
    public Trip WithLegs(IEnumerable<TripLeg> legs) => Create(Name, TimeZone, Start, End, IsRoundTrip, legs, TripId, Revision);

    /** longest trip name accepted. */
    public const int MaxNameLength = 200;

    /** most legs one trip may hold. */
    public const int MaxLegCount = 100;

    private static List<TripError> Validate(string name, TimeZoneInfo zone, DateTimeOffset start, DateTimeOffset end, bool isRoundTrip, List<TripLeg> legs)
    {
        var errors = new List<TripError>();
        void Fail(string field, string message) => errors.Add(new(field, message));
        if (string.IsNullOrWhiteSpace(name)) Fail("name", "name is required.");
        else if (name.Length > MaxNameLength) Fail("name", $"name must be at most {MaxNameLength} characters.");
        if (end < start) Fail("end", "trip end must not be before trip start.");
        if (legs.Count == 0)
        {
            Fail("legs", "a trip needs at least one leg.");
            return errors;
        }
        if (legs.Count > MaxLegCount)
        {
            Fail("legs", $"a trip may have at most {MaxLegCount} legs.");
            return errors;
        }
        for (var index = 0; index < legs.Count; index++)
        {
            var leg = legs[index];
            var path = $"legs[{index}]";
            if (leg.Sequence != index + 1) Fail($"{path}.sequence", $"expected sequence {index + 1} but got {leg.Sequence}; legs must be ordered without gaps.");
            if (leg.Transport.TransportModeId == Guid.Empty) Fail($"{path}.transport", "a transport mode is required.");
            if (leg.Arrival <= leg.Departure) Fail($"{path}.arrival", "arrival must be after departure.");
            if (leg.Departure < start) Fail($"{path}.departure", "departure is before the trip start.");
            if (leg.Arrival > end) Fail($"{path}.arrival", "arrival is after the trip end.");
            CheckZoneOffset(leg.Departure, zone, $"{path}.departure", Fail);
            CheckZoneOffset(leg.Arrival, zone, $"{path}.arrival", Fail);
            if (index == 0) continue;
            var previous = legs[index - 1];
            if (leg.From.PlaceId != previous.To.PlaceId) Fail($"{path}.from", "must start where the previous leg ended.");
            if (leg.Departure < previous.Arrival) Fail($"{path}.departure", "overlaps the previous leg; depart at or after its arrival.");
        }
        var first = legs[0];
        var last = legs[^1];
        if (first.From.Type != TripPlaceTypes.Accommodation) Fail("legs[0].from", "a trip must start at an accommodation.");
        if (isRoundTrip)
        {
            if (last.To.PlaceId != first.From.PlaceId) Fail($"legs[{legs.Count - 1}].to", "a round trip must return to where it started.");
            if (!legs.Any(leg => leg.To.Type == TripPlaceTypes.Attraction)) Fail("legs", "a round trip must include at least one attraction.");
        }
        else if (last.To.Type != TripPlaceTypes.Attraction) Fail($"legs[{legs.Count - 1}].to", "a one-way trip must end at an attraction.");
        return errors;
    }

    // a time whose offset disagrees with the trip zone was entered wrongly; report it rather than fix it
    private static void CheckZoneOffset(DateTimeOffset time, TimeZoneInfo zone, string field, Action<string, string> fail)
    {
        var local = time.DateTime;
        var valid = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Contains(time.Offset) : !zone.IsInvalidTime(local) && zone.GetUtcOffset(local) == time.Offset;
        if (!valid) fail(field, $"{time:yyyy-MM-ddTHH:mm:sszzz} is not a valid time in {zone.Id}; enter the time with an offset that {zone.Id} uses at that moment.");
    }
}
