using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Features.Trips;

/** Editable trip data shared by Map View, the standard editor and review. */
internal sealed class TripPlannerDraft
{
    readonly List<Stop> stops = [];
    readonly Dictionary<(Stop From, Stop To), Leg> legs = [];
    DateTimeOffset? savedStart;
    DateTimeOffset? savedEnd;
    string? savedZone;

    internal Guid? Id { get; set; }
    internal string Name { get; set; } = "";
    internal string TimeZone { get; set; } = "America/Toronto";
    internal DateTime? Start { get; set; }
    internal DateTime? End { get; set; }
    internal bool RoundTrip { get; set; }
    internal IReadOnlyList<Stop> Stops => stops;

    /** A distinct occurrence of a place in the itinerary. */
    internal sealed class Stop(TripPlaceDto place)
    {
        internal TripPlaceDto Place { get; set; } = place;
    }

    /** The plan for one specific pair of adjacent stop occurrences. */
    internal sealed class Leg
    {
        internal TripDto.Mode? Mode { get; set; }
        internal DateTime? Departure { get; set; }
        internal DateTime? Arrival { get; set; }
        internal int? DurationMinutes { get; set; }
        internal DateTimeOffset? SavedDeparture { get; init; }
        internal DateTimeOffset? SavedArrival { get; init; }
    }

    /** Inserts a place after the selected stop, or appends it when no insertion point is supplied. */
    internal int Add(TripPlaceDto place, int? after = null)
    {
        var index = after is { } previous ? Math.Clamp(previous + 1, 0, stops.Count) : stops.Count;
        stops.Insert(index, new Stop(place));
        return index;
    }

    /** Removes the stop and its associated leg edits. */
    internal void Remove(int index)
    {
        var stop = stops[index];
        foreach (var key in legs.Keys.Where(key => key.From == stop || key.To == stop).ToArray())
            legs.Remove(key);
        stops.RemoveAt(index);
    }

    /** Moves the stop and retains the details for any leg whose endpoints remain adjacent. */
    internal void Move(int index, int destination) =>
        (stops[index], stops[destination]) = (stops[destination], stops[index]);

    /** Retrieves the editable leg ending at the given stop. */
    internal Leg LegTo(int destination)
    {
        var key = (stops[destination - 1], stops[destination]);
        if (!legs.TryGetValue(key, out var leg))
        {
            leg = new Leg();
            legs.Add(key, leg);
        }
        return leg;
    }

    /** Restores the shared draft from the stored trip and its ordered snapshots. */
    internal static TripPlannerDraft FromSaved(TripDto trip)
    {
        var draft = new TripPlannerDraft
        {
            Id = trip.TripId,
            Name = trip.Name,
            TimeZone = trip.TimeZone,
            Start = trip.Start.DateTime,
            End = trip.End.DateTime,
            RoundTrip = trip.IsRoundTrip,
            savedStart = trip.Start,
            savedEnd = trip.End,
            savedZone = trip.TimeZone
        };
        foreach (var leg in trip.Legs.OrderBy(leg => leg.Sequence))
        {
            if (draft.stops.Count == 0)
                draft.Add(leg.From);
            var destination = draft.Add(leg.To);
            draft.legs[(draft.stops[destination - 1], draft.stops[destination])] = new Leg
            {
                Mode = leg.Transport,
                Departure = leg.Departure.DateTime,
                Arrival = leg.Arrival.DateTime,
                SavedDeparture = leg.Departure,
                SavedArrival = leg.Arrival
            };
        }
        return draft;
    }

    /** Gets the displayed arrival, calculated from elapsed duration in the trip's time zone. */
    internal DateTime? ArrivalFor(Leg leg)
    {
        if (leg.DurationMinutes is not { } minutes)
            return leg.Arrival;
        if (leg.Departure is not { } departure || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone))
            return null;

        var start = ResolveLocal(departure, leg.SavedDeparture, zone);
        return ArrivalAfter(start, minutes, zone)?.DateTime;
    }

    DateTimeOffset? ResolveLocal(DateTime time, DateTimeOffset? savedTime, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
        if (TimeZone.Trim() == savedZone && savedTime is { } saved && local == saved.DateTime)
            return saved;
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            return null;
        var offset = zone.GetUtcOffset(local);
        var utcTicks = local.Ticks - offset.Ticks;
        return utcTicks >= DateTime.MinValue.Ticks && utcTicks <= DateTime.MaxValue.Ticks
            ? new DateTimeOffset(local, offset) : null;
    }

    static DateTimeOffset? ArrivalAfter(DateTimeOffset? start, int minutes, TimeZoneInfo zone)
    {
        if (start is not { } departure || minutes <= 0 || minutes > (DateTimeOffset.MaxValue - departure).TotalMinutes)
            return null;
        return TimeZoneInfo.ConvertTime(departure.ToUniversalTime().AddMinutes(minutes), zone);
    }

    /**
     * Checks draft fields and builds the request. Empty catalogue identifiers can be resolved after validation.
     * @return The request when valid and an ordered list of validation messages otherwise.
     */
    internal (SaveTripRequest? Request, IReadOnlyList<string> Errors) Build()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Enter a trip name.");
        var zoneId = TimeZone.Trim();
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone))
            errors.Add("Enter a valid time zone, such as America/Toronto.");
        var start = Resolve(Start, savedStart, "Trip start");
        var end = Resolve(End, savedEnd, "Trip end");
        if (start > end)
            errors.Add("Trip end must be at or after trip start.");

        if (stops.Count < 2)
            errors.Add("Add at least two stops.");
        else
        {
            var first = stops[0].Place;
            var last = stops[^1].Place;
            if (first.Type != "accommodation")
                errors.Add("Start the trip at an accommodation.");
            if (RoundTrip)
            {
                if (first.PlaceId == Guid.Empty ? first != last : first.PlaceId != last.PlaceId || first.Type != last.Type)
                    errors.Add("Return to the starting accommodation for a round trip.");
                if (!stops.Any(stop => stop.Place.Type == "attraction"))
                    errors.Add("Include at least one attraction in a round trip.");
            }
            else if (last.Type != "attraction")
                errors.Add("End the one-way trip at an attraction.");
        }

        var requestLegs = new List<SaveTripRequest.Leg>();
        DateTimeOffset? previousArrival = null;
        for (var index = 1; index < stops.Count; index++)
        {
            var leg = LegTo(index);
            if (leg.Mode is null)
                errors.Add($"Select transport for connection {index}.");
            var departure = Resolve(leg.Departure, leg.SavedDeparture, $"Connection {index} departure");
            DateTimeOffset? arrival;
            if (leg.DurationMinutes is { } minutes)
            {
                arrival = zone is null ? null : ArrivalAfter(departure, minutes, zone);
                if (departure is not null && arrival is null)
                    errors.Add($"Connection {index} needs a duration that fits the trip calendar.");
            }
            else
            {
                arrival = Resolve(leg.Arrival, leg.SavedArrival, $"Connection {index} arrival");
            }
            if (departure < start || arrival > end)
                errors.Add($"Connection {index} must be within the trip dates.");
            if (arrival <= departure)
                errors.Add($"Connection {index} must arrive after departure.");
            if (departure < previousArrival)
                errors.Add($"Connection {index} overlaps the previous connection.");
            previousArrival = arrival;
            if (leg.Mode is not null && departure is not null && arrival is not null)
                requestLegs.Add(new(index, Reference(stops[index - 1].Place), Reference(stops[index].Place),
                    leg.Mode.TransportModeId, departure.Value, arrival.Value));
        }

        return errors.Count == 0 && start is not null && end is not null
            ? (new(Name.Trim(), zoneId, start.Value, end.Value, RoundTrip, requestLegs), errors)
            : (null, errors);

        DateTimeOffset? Resolve(DateTime? localTime, DateTimeOffset? savedTime, string field)
        {
            if (localTime is null)
            {
                errors.Add($"Enter {field.ToLowerInvariant()}.");
                return null;
            }
            if (zone is null)
                return null;

            var resolved = ResolveLocal(localTime.Value, savedTime, zone);
            if (resolved is null)
                errors.Add($"{field} needs a valid, unambiguous local time.");
            return resolved;
        }

        static SaveTripRequest.Place Reference(TripPlaceDto place) => new(place.PlaceId, place.Type);
    }
}
