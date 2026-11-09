using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Features.Trips;

/** Editable trip data shared by Map View, the standard editor and review. */
internal sealed class TripPlannerDraft
{
    readonly List<Stop> stops = [];
    readonly Dictionary<(Stop From, Stop To), Leg> legs = [];
    string? savedZone;
    bool? roundTrip;

    internal Guid? Id { get; set; }
    internal string? ETag { get; private set; }
    internal string Name { get; set; } = "";
    internal string TimeZone { get; set; } = "America/Toronto";
    internal DateTime? Start { get; set; }
    internal DateTime? End { get; set; }
    internal DateTimeOffset? SavedStart { get; private set; }
    internal DateTimeOffset? SavedEnd { get; private set; }
    internal OffsetChoice? StartOffset { get; set; }
    internal OffsetChoice? EndOffset { get; set; }
    internal bool RoundTrip
    {
        get => roundTrip ?? ReturnsToStart;
        set => roundTrip = value;
    }
    internal IReadOnlyList<Stop> Stops => stops;

    /** An explicit offset selection scoped to its local time and trip time zone. */
    internal sealed record OffsetChoice(DateTime Time, string Zone, TimeSpan Offset);

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
        internal object LocationIdentity => location;

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
        internal OffsetChoice? DepartureOffset { get; set; }
        internal OffsetChoice? ArrivalOffset { get; set; }

        internal Leg Copy() => (Leg)MemberwiseClone();
    }

    internal sealed record BrowserSnapshot(Guid? Id, string? ETag, string Name, string TimeZone,
        DateTime? Start, DateTime? End, DateTimeOffset? SavedStart, DateTimeOffset? SavedEnd, string? SavedZone,
        OffsetChoice? StartOffset, OffsetChoice? EndOffset, bool? RoundTrip, BrowserStop[] Stops, BrowserLeg[] Legs);

    internal sealed record BrowserStop(TripPlaceDto Place, int Location);

    internal sealed record BrowserLeg(int From, int To, TripDto.Mode? Mode, DateTime? Departure, DateTime? Arrival,
        int? DurationMinutes, DateTimeOffset? SavedDeparture, DateTimeOffset? SavedArrival,
        OffsetChoice? DepartureOffset, OffsetChoice? ArrivalOffset);

    /** Captures the editable map, including disconnected stops and unfinished legs. */
    internal BrowserSnapshot CaptureBrowserSnapshot()
    {
        var indices = stops.Select((stop, index) => (stop, index)).ToDictionary(pair => pair.stop, pair => pair.index);
        var locations = new Dictionary<object, int>();
        var savedStops = stops.Select((stop, index) =>
        {
            locations.TryAdd(stop.LocationIdentity, index);
            return new BrowserStop(stop.Place, locations[stop.LocationIdentity]);
        }).ToArray();
        var savedLegs = legs.Select(pair => new BrowserLeg(indices[pair.Key.From], indices[pair.Key.To], pair.Value.Mode,
            pair.Value.Departure, pair.Value.Arrival, pair.Value.DurationMinutes, pair.Value.SavedDeparture,
            pair.Value.SavedArrival, pair.Value.DepartureOffset, pair.Value.ArrivalOffset)).ToArray();
        return new(Id, ETag, Name, TimeZone, Start, End, SavedStart, SavedEnd, savedZone,
            StartOffset, EndOffset, roundTrip, savedStops, savedLegs);
    }

    /** Reconstructs the editable map without applying completed-trip validation. */
    internal static TripPlannerDraft FromBrowserSnapshot(BrowserSnapshot snapshot)
    {
        if (snapshot.Stops is null || snapshot.Legs is null || snapshot.Name is null || snapshot.TimeZone is null)
            throw new InvalidDataException("The browser draft is missing required fields.");

        var restored = new TripPlannerDraft
        {
            Id = snapshot.Id, ETag = snapshot.ETag, Name = snapshot.Name, TimeZone = snapshot.TimeZone,
            Start = snapshot.Start, End = snapshot.End, SavedStart = snapshot.SavedStart, SavedEnd = snapshot.SavedEnd,
            savedZone = snapshot.SavedZone, StartOffset = snapshot.StartOffset, EndOffset = snapshot.EndOffset,
            roundTrip = snapshot.RoundTrip
        };
        foreach (var stop in snapshot.Stops)
        {
            if (stop?.Place is null || stop.Location < 0 || stop.Location > restored.stops.Count)
                throw new InvalidDataException("The browser draft contains an invalid stop reference.");
            if (stop.Location < restored.stops.Count && restored.stops[stop.Location].Place != stop.Place)
                throw new InvalidDataException("The browser draft has inconsistent details for a shared stop.");
            restored.stops.Add(stop.Location < restored.stops.Count ? restored.stops[stop.Location].Revisit() : new(stop.Place));
        }

        foreach (var leg in snapshot.Legs)
        {
            if (leg is null || leg.From == leg.To || (uint)leg.From >= (uint)restored.stops.Count ||
                (uint)leg.To >= (uint)restored.stops.Count)
                throw new InvalidDataException("The browser draft contains an invalid connection reference.");
            if (!restored.legs.TryAdd((restored.stops[leg.From], restored.stops[leg.To]), new Leg
            {
                Mode = leg.Mode, Departure = leg.Departure, Arrival = leg.Arrival, DurationMinutes = leg.DurationMinutes,
                SavedDeparture = leg.SavedDeparture, SavedArrival = leg.SavedArrival,
                DepartureOffset = leg.DepartureOffset, ArrivalOffset = leg.ArrivalOffset
            }))
                throw new InvalidDataException("The browser draft contains a duplicate connection.");
        }
        return restored;
    }

    /** @return A one-time restore of the current draft, including shared locations and leg details. */
    internal Action CaptureUndo()
    {
        var places = stops.Select(stop => (Stop: stop, stop.Place)).ToArray();
        var connections = legs.Select(pair => (pair.Key, Value: pair.Value.Copy())).ToArray();
        var basics = (Name, TimeZone, Start, End, StartOffset, EndOffset, roundTrip);

        return () =>
        {
            (Name, TimeZone, Start, End, StartOffset, EndOffset, roundTrip) = basics;
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

    /**
     * Appends another visit to the starting accommodation with the same editable location.
     * @return The index of the return visit.
     */
    internal int AddReturnToStart()
    {
        RoundTrip = true;
        stops.Add(stops[0].Revisit());
        return stops.Count - 1;
    }

    /**
     * Edits the location of a visit and keeps matching round-trip endpoints synchronized.
     * @param stop The occurrence being edited.
     * @param place The new location details.
     */
    internal void UpdatePlace(Stop stop, TripPlaceDto place)
    {
        if (RoundTrip && stops.Count > 1 && SamePlace(stops[0].Place, stops[^1].Place))
        {
            if (stop == stops[0])
                stops[^1].Place = place;
            else if (stop == stops[^1])
                stops[0].Place = place;
        }
        stop.Place = place;
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
    internal static TripPlannerDraft FromSaved(TripRevision revision)
    {
        var trip = revision.Trip;
        var draft = new TripPlannerDraft
        {
            Id = trip.TripId,
            ETag = revision.ETag,
            Name = trip.Name,
            TimeZone = trip.TimeZone,
            Start = trip.Start.DateTime,
            End = trip.End.DateTime,
            SavedStart = trip.Start,
            SavedEnd = trip.End,
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

        var start = ResolveLocal(leg.Departure, leg.SavedDeparture, leg.DepartureOffset, zone);
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
        var departure = ResolveLocal(outbound.Departure, outbound.SavedDeparture, outbound.DepartureOffset, zone);
        return departure - arrival is { Ticks: >= 0 } duration ? duration : null;
    }

    DateTimeOffset? ArrivalInZone(Leg leg, TimeZoneInfo zone) => leg.DurationMinutes is { } minutes
        ? ArrivalAfter(ResolveLocal(leg.Departure, leg.SavedDeparture, leg.DepartureOffset, zone), minutes, zone)
        : ResolveLocal(leg.Arrival, leg.SavedArrival, leg.ArrivalOffset, zone);

    /**
     * Finds the two valid offsets and any saved or explicitly selected offset for a repeated local time.
     * @param time Local wall-clock time.
     * @param savedTime Original server-confirmed date and offset for the field.
     * @param choice Current explicit selection.
     * @return The offsets in occurrence order and the selection, or no offsets outside a repeated hour.
     */
    internal (TimeSpan[] Options, TimeSpan? Selected) RepeatedTime(DateTime? time, DateTimeOffset? savedTime, OffsetChoice? choice)
    {
        if (time is not { } value || !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone))
            return ([], null);

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        return zone.IsAmbiguousTime(local)
            ? (zone.GetAmbiguousTimeOffsets(local).OrderDescending().ToArray(), ResolveLocal(local, savedTime, choice, zone)?.Offset)
            : ([], null);
    }

    DateTimeOffset? ResolveLocal(DateTime? time, DateTimeOffset? savedTime, OffsetChoice? choice, TimeZoneInfo zone)
    {
        if (time is not { } value)
            return null;

        var local = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            return null;
        var offset = zone.GetUtcOffset(local);
        if (zone.IsAmbiguousTime(local))
        {
            var zoneId = TimeZone.Trim();
            var selected = choice is not null && choice.Time == local && choice.Zone == zoneId ? choice.Offset
                : zoneId == savedZone && savedTime is { } saved && local == saved.DateTime ? saved.Offset : (TimeSpan?)null;
            if (selected is not { } chosen || !zone.GetAmbiguousTimeOffsets(local).Contains(chosen))
                return null;
            offset = chosen;
        }
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
        var start = Resolve(Start, SavedStart, StartOffset, "Trip start");
        var end = Resolve(End, SavedEnd, EndOffset, "Trip end");
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
            var departure = Resolve(leg.Departure, leg.SavedDeparture, leg.DepartureOffset, $"Connection {index} departure");
            DateTimeOffset? arrival;
            if (leg.DurationMinutes is { } minutes)
            {
                arrival = zone is null ? null : ArrivalAfter(departure, minutes, zone);
                if (departure is not null && arrival is null)
                    errors.Add($"Connection {index} needs a duration that fits the trip calendar.");
            }
            else
            {
                arrival = Resolve(leg.Arrival, leg.SavedArrival, leg.ArrivalOffset, $"Connection {index} arrival");
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

        DateTimeOffset? Resolve(DateTime? localTime, DateTimeOffset? savedTime, OffsetChoice? choice, string field)
        {
            if (localTime is null)
            {
                errors.Add($"Enter {field.ToLowerInvariant()}.");
                return null;
            }
            if (zone is null)
                return null;

            var resolved = ResolveLocal(localTime.Value, savedTime, choice, zone);
            if (resolved is null)
                errors.Add(zone.IsAmbiguousTime(DateTime.SpecifyKind(localTime.Value, DateTimeKind.Unspecified))
                    ? $"Choose a UTC offset for {field.ToLowerInvariant()}."
                    : $"{field} needs a valid local time.");
            return resolved;
        }

        static SaveTripRequest.Place Reference(TripPlaceDto place) => new(place.PlaceId, place.Type);
    }
}
