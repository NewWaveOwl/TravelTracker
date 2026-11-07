using System.Security.Cryptography;
using System.Text;
using TravelTracker.Trips.Domain.Models;

namespace TravelTracker.Trips.Domain.Tests;

/**
 * Verifies the pure trip itinerary rules; no host, no storage.
 */
public sealed class TripTests {
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
    private static readonly DateTimeOffset Day = new(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));

    /**
     * Derives a stable identifier so the same place or mode keeps the same id across a test.
     * @param key The place type or transport code.
     * @return A deterministic GUID for the key.
     */
    private static Guid Stable(string key) => new(MD5.HashData(Encoding.UTF8.GetBytes(key)));

    private static TripPlace Point(string type) => new(Stable(type), type, type, "addr", 1m, 2m);

    private static TripLeg Leg(int sequence, TripPlace from, TripPlace to, int departHour, int arriveHour, string code = "train") =>
        new(sequence, from, to, new(Stable(code), code, code), Day.AddHours(departHour), Day.AddHours(arriveHour));

    private static Trip OneWay(TripPlace hotel, TripPlace museum) =>
        Trip.Create("t", Zone, Day, Day.AddHours(10), false, [Leg(1, hotel, museum, 1, 3)]);

    private static List<string> RejectedFields(Action action) =>
        Assert.Throws<TripValidationException>(action).Errors.Select(error => error.Field).ToList();

    /**
     * Accepts the minimum one-way and round-trip itineraries.
     */
    [Fact]
    public void Create_ValidOneWayAndRoundTrip_Accepted() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction"), stop = Point("transport_stop");
        Assert.Single(OneWay(hotel, museum).Legs);
        var round = Trip.Create("t", Zone, Day, Day.AddHours(10), true,
            [Leg(1, hotel, stop, 1, 2), Leg(2, stop, museum, 2, 4, "bus"), Leg(3, museum, hotel, 5, 6, "car")]);
        Assert.Equal(3, round.Legs.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<TripLeg>)round.Legs).Clear());
    }

    /**
     * Rejects endpoint mistakes for one-way and round trips.
     */
    [Fact]
    public void Create_WrongEndpoints_Rejected() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction");
        Assert.Contains("legs[0].to", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false, [Leg(1, hotel, hotel, 1, 2)])));
        Assert.Contains("legs[0].to", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), true, [Leg(1, hotel, museum, 1, 2)])));
        Assert.Contains("legs[0].from", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false, [Leg(1, museum, museum, 1, 2)])));
    }

    /**
     * Rejects disconnected, misordered, reversed and overlapping legs.
     */
    [Fact]
    public void Create_BrokenLegs_ReportsEachField() {
        TripPlace hotel = Point("accommodation"), stop = Point("transport_stop"), museum = Point("attraction");
        var fields = RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false,
            [Leg(1, hotel, stop, 1, 3), Leg(3, hotel, museum, 2, 2)]));
        Assert.Contains("legs[1].sequence", fields);
        Assert.Contains("legs[1].from", fields);
        Assert.Contains("legs[1].departure", fields);
        Assert.Contains("legs[1].arrival", fields);
    }

    /**
     * Rejects legs outside the trip bounds, a missing transport mode and empty trips.
     */
    [Fact]
    public void Create_OutsideBoundsOrMissingMode_Rejected() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction");
        var leg = new TripLeg(1, hotel, museum, new(Guid.Empty, "", ""), Day.AddHours(-1), Day.AddHours(5));
        var fields = RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(4), false, [leg]));
        Assert.Contains("legs[0].departure", fields);
        Assert.Contains("legs[0].arrival", fields);
        Assert.Contains("legs[0].transport", fields);
        Assert.Contains("legs", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(4), false, [])));
    }

    /**
     * Resolves local times without guessing at clock changes.
     */
    [Fact]
    public void Resolve_ImpossibleAmbiguousAndNormal_NeverCorrectedSilently() {
        Assert.Throws<TripValidationException>(() => TripTime.Resolve(new(2026, 3, 29, 2, 30, 0), Zone));
        var ambiguous = new DateTime(2026, 10, 25, 2, 30, 0);
        Assert.Throws<TripValidationException>(() => TripTime.Resolve(ambiguous, Zone));
        Assert.Throws<TripValidationException>(() => TripTime.Resolve(ambiguous, Zone, TimeSpan.FromHours(5)));
        Assert.Equal(TimeSpan.FromHours(1), TripTime.Resolve(ambiguous, Zone, TimeSpan.FromHours(1)).Offset);
        Assert.Equal(TimeSpan.FromHours(2), TripTime.Resolve(new(2026, 6, 1, 8, 0, 0), Zone).Offset);
        Assert.Throws<TripValidationException>(() => TripTime.Resolve(new(2026, 6, 1, 8, 0, 0), Zone, TimeSpan.FromHours(5)));
        Assert.Equal(TimeSpan.FromHours(2), TripTime.Resolve(new(2026, 6, 1, 8, 0, 0), Zone, TimeSpan.FromHours(2)).Offset);
    }

    /**
     * Rejects a leg whose offset does not match the trip zone.
     */
    [Fact]
    public void Create_WrongOffsetForZone_Rejected() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction");
        var leg = new TripLeg(1, hotel, museum, new(Stable("bus"), "bus", "bus"), new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero), new(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
        Assert.Contains("legs[0].departure", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false, [leg])));
    }

    /**
     * Leaves the original trip unchanged when an edit is rejected.
     */
    [Fact]
    public void WithLegs_Rejected_LeavesOriginalUnchanged() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction");
        var trip = OneWay(hotel, museum);
        var before = trip.Legs.ToList();
        Assert.Throws<TripValidationException>(() => trip.WithLegs([Leg(1, hotel, museum, 3, 1)]));
        Assert.Equal(before, trip.Legs);
        Assert.Equal(Day.AddHours(2), trip.WithLegs([Leg(1, hotel, museum, 1, 2)]).Legs[0].Arrival);
    }
}
