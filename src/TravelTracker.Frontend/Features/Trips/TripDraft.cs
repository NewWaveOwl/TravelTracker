using System.Globalization;
using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Features.Trips;

/**
 * The trip being assembled in memory. It mirrors the Trips service rules so the form can point at a mistake before a request,
 * but the service stays authoritative and nothing here corrects what the traveller typed.
 */
public sealed class TripDraft
{
    const string LocalFormat = "yyyy-MM-dd'T'HH:mm";

    /** Maximum trip name length accepted by the Trips API. */
    public const int MaxNameLength = 200;

    /** Trip name. */
    public string Name { get; set; } = "";

    /** IANA time zone id every time below is read in. */
    public string TimeZone { get; set; } = "UTC";

    /** Inclusive start date in the trip time zone. */
    public DateOnly? Start { get; set; }

    /** Inclusive end date in the trip time zone. */
    public DateOnly? End { get; set; }

    /** The traveller's round-trip intent; saving does not require a return leg. */
    public bool IsRoundTrip { get; set; }

    /** Ordered legs. */
    public List<TripLegDraft> Legs { get; } = [];

    /** true once the traveller has entered anything worth warning about before leaving; the name, either date or any leg counts. */
    public bool HasEntries => Name.Length > 0 || Start is not null || End is not null || Legs.Count > 0;

    /**
     * Checks the name, time zone and trip dates.
     * @return Field errors keyed like the service's, empty when the basics are fine.
     */
    public Dictionary<string, string[]> ValidateBasics()
    {
        var errors = new Errors();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("name", "Enter a trip name.");
        else if (Name.Trim().Length > MaxNameLength) errors.Add("name", $"Use {MaxNameLength} characters or fewer for the trip name.");
        var zone = FindZone();
        if (zone is null) errors.Add("timeZone", "Enter a time zone this app recognizes, such as Europe/Paris.");
        var start = ReadBoundary(Start, zone, false, errors);
        var end = ReadBoundary(End, zone, true, errors);
        if (start is not null && end is not null && end < start) errors.Add("end", "The trip end must not be before the trip start.");
        return errors.ToDictionary();
    }

    /**
     * Checks every leg and the trip shape, with no silent repair of a disconnected or overlapping itinerary.
     * @return Field errors keyed like the service's, empty when the itinerary satisfies the trip rules.
     */
    public Dictionary<string, string[]> ValidateItinerary()
    {
        var errors = new Errors();
        var zone = FindZone();
        var start = ReadBoundary(Start, zone, false, null);
        var end = ReadBoundary(End, zone, true, null);
        DateTimeOffset? previousArrival = null;
        for (var index = 0; index < Legs.Count; index++)
        {
            var leg = Legs[index];
            var path = $"legs[{index}]";
            if (leg.From is null) errors.Add($"{path}.from", "Choose where this leg starts.");
            if (leg.To is null) errors.Add($"{path}.to", "Choose where this leg ends.");
            if (leg.TransportModeId is null) errors.Add($"{path}.transport", "Choose a transport mode.");
            var departure = Read(leg.Departure, zone, $"{path}.departure", errors);
            var arrival = Read(leg.Arrival, zone, $"{path}.arrival", errors);
            if (departure is not null && arrival is not null && arrival <= departure) errors.Add($"{path}.arrival", "Arrival must be after departure.");
            if (start is not null && departure is not null && departure < start) errors.Add($"{path}.departure", "Departure is before the trip start.");
            if (end is not null && arrival is not null && arrival > end) errors.Add($"{path}.arrival", "Arrival is after the trip end.");
            if (index > 0)
            {
                var previous = Legs[index - 1];
                if (leg.From is not null && previous.To is not null && !SamePlace(leg.From, previous.To))
                    errors.Add($"{path}.from", "This leg must start where the previous leg ended.");
                if (departure is not null && previousArrival is not null && departure < previousArrival)
                    errors.Add($"{path}.departure", "This leg overlaps the previous one; depart at or after its arrival.");
            }
            previousArrival = arrival;
        }
        return errors.ToDictionary();
    }

    /**
     * Builds the public create body from a draft that passed both validations.
     * @return The camelCase request with each time carrying its offset in the trip zone; no owner is included.
     */
    public SaveTripRequest ToRequest()
    {
        var zone = FindZone() ?? throw new InvalidOperationException("The draft has no valid time zone.");
        DateTimeOffset At(string local) => TryResolveInstant(local, zone, out var instant, out _) ? instant : throw new InvalidOperationException("The draft has an unusable time.");
        var start = ReadBoundary(Start, zone, false, null)!.Value;
        var end = ReadBoundary(End, zone, true, null)!.Value;
        return new SaveTripRequest(Name.Trim(), TimeZone.Trim(), start, end, IsRoundTrip,
            Legs.Select((leg, index) => new SaveTripRequest.Leg(index + 1,
                new(leg.From!.PlaceId, leg.From.Type), new(leg.To!.PlaceId, leg.To.Type),
                leg.TransportModeId!.Value, At(leg.Departure), At(leg.Arrival))).ToArray());
    }

    // a place is the same when its identifier and type match; choices always come from a service search, so an empty identifier never reaches here (unlike the map planner's rule)
    static bool SamePlace(TripPlaceChoice first, TripPlaceChoice second) => first.PlaceId == second.PlaceId && first.Type == second.Type;

    TimeZoneInfo? FindZone() =>
        !string.IsNullOrWhiteSpace(TimeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone.Trim(), out var zone) ? zone : null;

    static DateTimeOffset? ReadBoundary(DateOnly? day, TimeZoneInfo? zone, bool end, Errors? errors)
    {
        var value = day.HasValue && zone is not null ? TripDayBoundary.Resolve(day.Value, zone, end) : null;
        if (value is null && (zone is not null || day is null))
            errors?.Add(end ? "end" : "start", day is null
                ? (end ? "Choose the ending day." : "Choose the beginning day.")
                : "Choose dates that exist in the trip's time zone.");
        return value;
    }

    DateTimeOffset? Read(string local, TimeZoneInfo? zone, string? field, Errors errors)
    {
        if (zone is null) return null;
        if (string.IsNullOrWhiteSpace(local))
        {
            if (field is not null) errors.Add(field, "Enter a date and time.");
            return null;
        }
        if (TryResolveInstant(local, zone, out var instant, out var problem)) return instant;
        if (field is not null) errors.Add(field, problem);
        return null;
    }

    /**
     * Reads a local time in a zone without ever picking an offset for the traveller.
     * @param local A datetime-local value.
     * @param zone The trip time zone.
     * @param instant The time with the zone's offset when it is valid.
     * @param problem Why the time cannot be used, when it cannot.
     * @return True when the local time exists exactly once in the zone.
     */
    public static bool TryResolveInstant(string local, TimeZoneInfo zone, out DateTimeOffset instant, out string problem)
    {
        instant = default;
        problem = "";
        if (!DateTime.TryParseExact(local, LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            problem = "Enter a valid date and time.";
            return false;
        }
        if (zone.IsInvalidTime(parsed))
        {
            problem = $"{parsed:yyyy-MM-dd HH:mm} does not exist in {zone.Id} because the clocks skip forward. Choose another time.";
            return false;
        }
        if (zone.IsAmbiguousTime(parsed))
        {
            problem = $"{parsed:yyyy-MM-dd HH:mm} happens twice in {zone.Id} because the clocks go back. Choose a time outside the repeated hour.";
            return false;
        }
        var offset = zone.GetUtcOffset(parsed);
        var utcTicks = parsed.Ticks - offset.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks)
        {
            problem = "Enter a date the calendar can represent in this time zone.";
            return false;
        }
        instant = new DateTimeOffset(parsed, offset);
        return true;
    }

    sealed class Errors
    {
        readonly Dictionary<string, List<string>> fields = [];

        public void Add(string field, string message)
        {
            if (!fields.TryGetValue(field, out var list)) fields[field] = list = [];
            if (!list.Contains(message)) list.Add(message);
        }

        public Dictionary<string, string[]> ToDictionary() => fields.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}
