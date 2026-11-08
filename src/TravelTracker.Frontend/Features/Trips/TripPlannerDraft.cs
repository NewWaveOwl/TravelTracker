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
    bool? roundTrip;

    internal Guid? Id { get; set; }
    internal string Name { get; set; } = "";
    internal string TimeZone { get; set; } = "America/Toronto";
    internal DateTime? Start { get; set; }
    internal DateTime? End { get; set; }
    internal bool RoundTrip
    {
        get => roundTrip ?? ReturnsToStart;
        set => roundTrip = value;
    }
    internal IReadOnlyList<Stop> Stops => stops;

    /** A distinct occurrence of a place in the itinerary. */
    internal sealed class Stop
    {
        readonly Location location;

        internal Stop(TripPlaceDto place) : this(new Location(place)) { }

        Stop(Location location) => this.location = location;

        internal TripPlaceDto Place
        {
            get => location.Place;
            set => location.Place = value;
        }

        internal Stop Revisit() => new(location);

        sealed class Location(TripPlaceDto place)
        {
            internal TripPlaceDto Place { get; set; } = place;
        }
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

        internal Leg Copy() => (Leg)MemberwiseClone();
    }

    /** @return A one-time restore of the current draft, including shared locations and leg details. */
    internal Action CaptureUndo()
    {
        var places = stops.Select(stop => (Stop: stop, stop.Place)).ToArray();
        var connections = legs.Select(pair => (pair.Key, Value: pair.Value.Copy())).ToArray();
        var basics = (Name, TimeZone, Start, End, roundTrip);

        return () =>
        {
            (Name, TimeZone, Start, End, roundTrip) = basics;
            stops.Clear();
            foreach (var (stop, place) in places)
            {
                stop.Place = place;
                stops.Add(stop);
            }
            legs.Clear();
            foreach (var (key, leg) in connections)
                legs.Add(key, leg);
        };
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

    /**
     * Joins two selected places in the clicked direction while retaining existing connections.
     * @param from Index of the departure stop.
     * @param to Index of the destination stop.
     * @return Destination index in the resulting itinerary.
     */
    internal int JoinStops(int from, int to)
    {
        if (to == from + 1)
            return to;

        var source = stops[from];
        var target = stops[to];
        var sections = new List<List<Stop>>();
        for (var index = 0; index < stops.Count; index++)
        {
            if (index == 0 || !legs.TryGetValue((stops[index - 1], stops[index]), out var connection) || connection.Mode is null)
                sections.Add([]);
            sections[^1].Add(stops[index]);
        }

        var departure = sections.FirstOrDefault(section => section[^1] == source)
            ?? sections.FirstOrDefault(section => SamePlace(section[^1].Place, source.Place));
        var arrival = sections.FirstOrDefault(section => section != departure && section[0] == target)
            ?? sections.FirstOrDefault(section => section != departure && SamePlace(section[0].Place, target.Place));
        if (arrival is null && departure is not null && SamePlace(departure[0].Place, target.Place))
        {
            var accommodation = departure.FindIndex(stop => stop.Place.Type == "accommodation");
            if (accommodation > 0)
                return StartLoopAtAccommodation(departure, accommodation);
        }

        // When a visit already has a connection in this direction, start or end a separate visit.
        var joined = departure ?? [source.Revisit()];
        var destination = arrival?[0] ?? target.Revisit();
        var insertAt = Math.Min(departure is null ? sections.Count : sections.IndexOf(departure),
            arrival is null ? sections.Count : sections.IndexOf(arrival));
        if (departure is not null)
            sections.Remove(departure);
        if (arrival is not null)
            sections.Remove(arrival);
        joined.AddRange(arrival ?? [destination]);
        sections.Insert(insertAt, joined);

        stops.Clear();
        stops.AddRange(sections.SelectMany(section => section));
        return stops.IndexOf(destination);
    }

    /** Rotates a newly closed loop so it begins and ends at an accommodation. */
    int StartLoopAtAccommodation(List<Stop> section, int startIndex)
    {
        var start = section[startIndex];
        var previous = section[startIndex - 1];
        var returnVisit = start.Revisit();
        legs[(previous, returnVisit)] = legs[(previous, start)];
        legs.Remove((previous, start));

        var offset = stops.IndexOf(section[0]);
        stops.RemoveRange(offset, section.Count);
        stops.InsertRange(offset, section.Skip(startIndex).Concat(section.Take(startIndex)).Append(returnVisit));
        return offset + section.Count - startIndex;
    }

    /** Indicates whether the ordered stops return to the starting accommodation. */
    bool ReturnsToStart => stops.Count > 1 && stops[0].Place.Type == "accommodation"
        && SamePlace(stops[0].Place, stops[^1].Place);

    static bool SamePlace(TripPlaceDto first, TripPlaceDto second) => first == second ||
        (first.PlaceId != Guid.Empty && first.PlaceId == second.PlaceId && first.Type == second.Type);

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
            savedStart = trip.Start,
            savedEnd = trip.End,
            savedZone = trip.TimeZone
        };
        var places = new Dictionary<TripPlaceDto, Stop>();
        foreach (var leg in trip.Legs.OrderBy(leg => leg.Sequence))
        {
            if (draft.stops.Count == 0)
            {
                draft.Add(leg.From);
                places.Add(leg.From, draft.stops[0]);
            }
            var destination = places.TryGetValue(leg.To, out var visit) ? visit.Revisit() : new Stop(leg.To);
            places.TryAdd(leg.To, destination);
            draft.legs[(draft.stops[^1], destination)] = new Leg
            {
                Mode = leg.Transport,
                Departure = leg.Departure.DateTime,
                Arrival = leg.Arrival.DateTime,
                SavedDeparture = leg.Departure,
                SavedArrival = leg.Arrival
            };
            draft.stops.Add(destination);
        }
        if (draft.RoundTrip != trip.IsRoundTrip)
            draft.RoundTrip = trip.IsRoundTrip;
        return draft;
    }

    /** Gets the displayed arrival, calculated from elapsed duration in the trip's time zone. */
    internal DateTime? ArrivalFor(Leg leg)
    {
        if (leg.DurationMinutes is null)
            return leg.Arrival;
        return TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone)
            ? ArrivalInZone(leg, zone)?.DateTime
            : null;
    }

    /** Gets the elapsed duration using the same local time resolution as saving. */
    internal TimeSpan? DurationFor(Leg leg)
    {
        if (leg.DurationMinutes is { } minutes)
            return TimeSpan.FromMinutes(minutes);
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone))
            return null;

        var start = ResolveLocal(leg.Departure, leg.SavedDeparture, zone);
        var end = ArrivalInZone(leg, zone);
        return end - start is { Ticks: > 0 } duration ? duration : null;
    }

    /**
     * Calculates the time at an intermediate stop between its inbound and outbound connections.
     * @param index The visit's index in the ordered itinerary.
     * @return Elapsed time when both boundaries are known and in order.
     */
    internal TimeSpan? DurationAtStop(int index)
    {
        if (index <= 0 || index >= stops.Count - 1 ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone))
            return null;

        var inbound = LegTo(index);
        var outbound = LegTo(index + 1);
        if (inbound.Mode is null || outbound.Mode is null)
            return null;

        var arrival = ArrivalInZone(inbound, zone);
        var departure = ResolveLocal(outbound.Departure, outbound.SavedDeparture, zone);
        return departure - arrival is { Ticks: >= 0 } duration ? duration : null;
    }

    DateTimeOffset? ArrivalInZone(Leg leg, TimeZoneInfo zone) => leg.DurationMinutes is { } minutes
        ? ArrivalAfter(ResolveLocal(leg.Departure, leg.SavedDeparture, zone), minutes, zone)
        : ResolveLocal(leg.Arrival, leg.SavedArrival, zone);

    DateTimeOffset? ResolveLocal(DateTime? time, DateTimeOffset? savedTime, TimeZoneInfo zone)
    {
        if (time is not { } value)
            return null;

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
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
                if (!SamePlace(first, last))
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
