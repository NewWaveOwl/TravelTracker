using System.Text.Json;
using TravelTracker.Frontend.Features.Trips;

namespace TravelTracker.Frontend.Tests;

/** The client-side trip rules: they mirror the service, point at the field, and never repair what was typed. */
public sealed class TripDraftTests
{
    static readonly TripPlaceChoice Hotel = new(Guid.NewGuid(), "accommodation", "Hotel", "1 Rue");
    static readonly TripPlaceChoice Museum = new(Guid.NewGuid(), "attraction", "Museum", "2 Rue");
    static readonly TripPlaceChoice Station = new(Guid.NewGuid(), "transport_stop", "Station", "3 Rue");

    static TripLegDraft Leg(TripPlaceChoice from, TripPlaceChoice to, string departure, string arrival) =>
        new() { From = from, To = to, TransportModeId = Guid.NewGuid(), TransportName = "Bus", Departure = departure, Arrival = arrival };

    static TripDraft Valid()
    {
        var draft = new TripDraft { Name = "Paris", TimeZone = "Europe/Paris", Start = "2026-06-01T08:00", End = "2026-06-01T20:00" };
        draft.Legs.Add(Leg(Hotel, Museum, "2026-06-01T09:00", "2026-06-01T10:00"));
        return draft;
    }

    [Fact]
    public void AcceptsAValidOneWayTrip()
    {
        var draft = Valid();
        Assert.Empty(draft.ValidateBasics());
        Assert.Empty(draft.ValidateItinerary());
    }

    [Fact]
    public void RequiresNameKnownZoneAndOrderedDates()
    {
        var draft = new TripDraft { Name = " ", TimeZone = "Mars/Olympus", Start = "", End = "" };
        var errors = draft.ValidateBasics();
        Assert.Contains("name", errors.Keys);
        Assert.Contains("timeZone", errors.Keys);
        var reversed = new TripDraft { Name = "x", TimeZone = "UTC", Start = "2026-06-02T08:00", End = "2026-06-01T08:00" };
        Assert.Contains("end", reversed.ValidateBasics().Keys);
        var missing = new TripDraft { Name = "x", TimeZone = "UTC" };
        Assert.Equal(["end", "start"], missing.ValidateBasics().Keys.Order());
    }

    [Fact]
    public void ReportsADisconnectedLegWithoutRepairingIt()
    {
        var draft = Valid();
        draft.Legs.Add(Leg(Station, Museum, "2026-06-01T11:00", "2026-06-01T12:00"));
        var errors = draft.ValidateItinerary();
        Assert.Contains("legs[1].from", errors.Keys);
        Assert.Equal(Station, draft.Legs[1].From);
    }

    [Fact]
    public void ReportsOverlappingAndOutOfRangeTimes()
    {
        var draft = Valid();
        draft.Legs.Add(Leg(Museum, Hotel, "2026-06-01T09:30", "2026-06-01T21:00"));
        var errors = draft.ValidateItinerary();
        Assert.Contains("legs[1].departure", errors.Keys);
        Assert.Contains("legs[1].arrival", errors.Keys);
    }

    [Fact]
    public void RejectsArrivalNotAfterDeparture()
    {
        var draft = Valid();
        draft.Legs[0].Arrival = "2026-06-01T09:00";
        Assert.Contains("legs[0].arrival", draft.ValidateItinerary().Keys);
    }

    [Fact]
    public void ChecksTripEndpointsForOneWayAndRoundTrips()
    {
        var notAccommodation = Valid();
        notAccommodation.Legs[0].From = Museum;
        Assert.Contains("legs[0].from", notAccommodation.ValidateItinerary().Keys);

        var oneWayEndingAtStop = Valid();
        oneWayEndingAtStop.Legs[0].To = Station;
        Assert.Contains("legs[0].to", oneWayEndingAtStop.ValidateItinerary().Keys);

        var roundTrip = Valid();
        roundTrip.IsRoundTrip = true;
        Assert.Contains("legs[0].to", roundTrip.ValidateItinerary().Keys);
        roundTrip.Legs.Add(Leg(Museum, Hotel, "2026-06-01T11:00", "2026-06-01T12:00"));
        Assert.Empty(roundTrip.ValidateItinerary());

        var noAttraction = new TripDraft { Name = "x", TimeZone = "UTC", Start = "2026-06-01T08:00", End = "2026-06-01T20:00", IsRoundTrip = true };
        noAttraction.Legs.Add(Leg(Hotel, Station, "2026-06-01T09:00", "2026-06-01T10:00"));
        noAttraction.Legs.Add(Leg(Station, Hotel, "2026-06-01T11:00", "2026-06-01T12:00"));
        Assert.Contains("legs", noAttraction.ValidateItinerary().Keys);
    }

    [Fact]
    public void RequiresALegAndEachLegChoice()
    {
        var empty = new TripDraft { Name = "x", TimeZone = "UTC", Start = "2026-06-01T08:00", End = "2026-06-01T20:00" };
        Assert.Contains("legs", empty.ValidateItinerary().Keys);
        empty.Legs.Add(new TripLegDraft());
        var errors = empty.ValidateItinerary();
        foreach (var key in new[] { "legs[0].from", "legs[0].to", "legs[0].transport", "legs[0].departure", "legs[0].arrival" }) Assert.Contains(key, errors.Keys);
    }

    [Fact]
    public void RefusesSkippedAndRepeatedLocalTimesInsteadOfPickingAnOffset()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
        Assert.False(TripDraft.ToOffset("2026-03-29T02:30", zone, out _, out var skipped));
        Assert.Contains("does not exist", skipped);
        Assert.False(TripDraft.ToOffset("2026-10-25T02:30", zone, out _, out var repeated));
        Assert.Contains("happens twice", repeated);
        Assert.True(TripDraft.ToOffset("2026-06-01T08:00", zone, out var instant, out _));
        Assert.Equal(TimeSpan.FromHours(2), instant.Offset);
        Assert.False(TripDraft.ToOffset("not a time", zone, out _, out var unreadable));
        Assert.Contains("valid date", unreadable);
    }

    [Fact]
    public void BuildsTheCreateBodyWithOffsetsAndNoOwner()
    {
        var draft = Valid();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(draft.ToRequest(), JsonSerializerOptions.Web));
        var root = json.RootElement;
        Assert.Equal("Paris", root.GetProperty("name").GetString());
        Assert.Equal("2026-06-01T08:00:00+02:00", root.GetProperty("start").GetString());
        Assert.False(root.TryGetProperty("owner", out _));
        var leg = root.GetProperty("legs")[0];
        Assert.Equal(1, leg.GetProperty("sequence").GetInt32());
        Assert.Equal("accommodation", leg.GetProperty("from").GetProperty("type").GetString());
        Assert.Equal("2026-06-01T10:00:00+02:00", leg.GetProperty("arrival").GetString());
    }

    [Fact]
    public void HasEntriesOnlyOnceSomethingWasTyped()
    {
        Assert.False(new TripDraft().HasEntries);
        Assert.True(new TripDraft { Name = "x" }.HasEntries);
    }
}
