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
        internal DateTimeOffset? SavedDeparture { get; init; }
        internal DateTimeOffset? SavedArrival { get; init; }
    }

    /** Appends a place and returns the index of the new stop. */
    internal int Add(TripPlaceDto place)
    {
        stops.Add(new Stop(place));
        return stops.Count - 1;
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
                errors.Add($"Select transport for leg {index}.");
            var departure = Resolve(leg.Departure, leg.SavedDeparture, $"Leg {index} departure");
            var arrival = Resolve(leg.Arrival, leg.SavedArrival, $"Leg {index} arrival");
            if (departure < start || arrival > end)
                errors.Add($"Leg {index} must be within the trip dates.");
            if (arrival <= departure)
                errors.Add($"Leg {index} must arrive after departure.");
            if (departure < previousArrival)
                errors.Add($"Leg {index} overlaps the previous leg.");
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

            var local = DateTime.SpecifyKind(localTime.Value, DateTimeKind.Unspecified);
            if (zoneId == savedZone && savedTime is not null && local == savedTime.Value.DateTime)
                return savedTime;
            if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            {
                errors.Add($"{field} falls during a clock change. Choose an unambiguous time.");
                return null;
            }
            return new DateTimeOffset(local, zone.GetUtcOffset(local));
        }

        static SaveTripRequest.Place Reference(TripPlaceDto place) => new(place.PlaceId, place.Type);
    }
}
