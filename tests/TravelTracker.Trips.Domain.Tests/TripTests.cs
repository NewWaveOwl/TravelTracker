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
     * Accepts every supported endpoint composition without requiring particular place categories.
     */
    [Theory]
    [InlineData("accommodation", "accommodation")]
    [InlineData("accommodation", "attraction")]
    [InlineData("accommodation", "transport_stop")]
    [InlineData("attraction", "accommodation")]
    [InlineData("attraction", "attraction")]
    [InlineData("attraction", "transport_stop")]
    [InlineData("transport_stop", "accommodation")]
    [InlineData("transport_stop", "attraction")]
    [InlineData("transport_stop", "transport_stop")]
    public void Create_AnySupportedEndpointComposition_Accepted(string fromType, string toType) {
        var from = Point(fromType);
        var to = Point(toType) with { PlaceId = Stable($"destination-{toType}") };

        var trip = Trip.Create("t", Zone, Day, Day.AddHours(10), false, [Leg(1, from, to, 1, 2)]);

        Assert.Equal(from, Assert.Single(trip.Legs).From);
        Assert.Equal(to, trip.Legs[0].To);
        Assert.Equal(new[] { from, to }, trip.Stops);
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
     * Rejects legs outside the trip bounds and a missing transport mode.
     */
    [Fact]
    public void Create_OutsideBoundsOrMissingMode_Rejected() {
        TripPlace hotel = Point("accommodation"), museum = Point("attraction");
        var leg = new TripLeg(1, hotel, museum, new(Guid.Empty, "", ""), Day.AddHours(-1), Day.AddHours(5));
        var fields = RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(4), false, [leg]));
        Assert.Contains("legs[0].departure", fields);
        Assert.Contains("legs[0].arrival", fields);
        Assert.Contains("legs[0].transport", fields);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_EmptyItinerary_AcceptsValidBasicsWithoutInventingStops(bool roundTrip) {
        var trip = Trip.Create("A plan", Zone, Day, Day, roundTrip, []);

        Assert.Empty(trip.Legs);
        Assert.Empty(trip.Stops);
        Assert.Equal(roundTrip, trip.IsRoundTrip);
        Assert.Equal(Day, trip.Start);
        Assert.Equal(Day, trip.End);
    }

    [Theory]
    [InlineData("accommodation", false)]
    [InlineData("attraction", false)]
    [InlineData("transport_stop", false)]
    [InlineData("accommodation", true)]
    [InlineData("attraction", true)]
    [InlineData("transport_stop", true)]
    public void Create_SingleStop_AcceptsEachSupportedTypeWithoutAVisitOrJourney(string type, bool roundTrip) {
        var place = Point(type);

        var trip = Trip.Create("One stop", Zone, Day, Day.AddHours(1), roundTrip, [], stops: [place]);

        Assert.Equal(place, Assert.Single(trip.Stops));
        Assert.Empty(trip.Legs);
        Assert.Equal(roundTrip, trip.IsRoundTrip);
    }

    [Fact]
    public void Create_RoundTripIntent_DoesNotRequireReturnLegOrAttraction() {
        var first = Point("transport_stop");
        var last = Point("accommodation");

        var trip = Trip.Create("Still planning", Zone, Day, Day.AddHours(10), true, [Leg(1, first, last, 1, 2)]);

        Assert.True(trip.IsRoundTrip);
        Assert.Equal(last, trip.Stops[^1]);
        Assert.NotEqual(first.PlaceId, trip.Stops[^1].PlaceId);
    }

    [Fact]
    public void Create_MultipleStopsWithoutLegs_ReportsMissingConnections() {
        Assert.Contains("stops", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false, [],
            stops: [Point("attraction"), Point("transport_stop")])));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("reversed")]
    [InlineData("different")]
    public void Create_ExplicitStopsDisagreeWithLegs_Rejected(string mismatch) {
        var from = Point("attraction");
        var to = Point("transport_stop");
        TripPlace[] stops = mismatch switch {
            "empty" => [],
            "reversed" => [to, from],
            _ => [from, Point("accommodation")]
        };

        Assert.Contains("stops", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false,
            [Leg(1, from, to, 1, 2)], stops: stops)));
    }

    [Fact]
    public void Create_ExplicitStopsMatchLegs_PreservesTheSnapshots() {
        var from = Point("attraction");
        var to = Point("transport_stop");

        var trip = Trip.Create("t", Zone, Day, Day.AddHours(10), false, [Leg(1, from, to, 1, 2)], stops: [from, to]);

        Assert.Equal(new[] { from, to }, trip.Stops);
    }

    [Fact]
    public void Create_MutableInputCollections_CannotChangeSavedStopOrLegOrder() {
        var place = Point("attraction");
        var stops = new List<TripPlace> { place };
        var legs = new List<TripLeg>();
        var trip = Trip.Create("t", Zone, Day, Day.AddHours(1), false, legs, stops: stops);

        stops.Clear();
        legs.Add(Leg(1, place, Point("transport_stop"), 0, 1));

        Assert.Equal(place, Assert.Single(trip.Stops));
        Assert.Empty(trip.Legs);
        Assert.Throws<NotSupportedException>(() => ((IList<TripPlace>)trip.Stops).Clear());
    }

    [Theory]
    [InlineData("placeId")]
    [InlineData("type")]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("lat")]
    [InlineData("lon")]
    public void Create_InvalidStandaloneSnapshot_ReportsTheInvalidField(string field) {
        var place = InvalidPlace(field);

        Assert.Contains($"stops[0].{field}", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(1), false, [], stops: [place])));
    }

    [Theory]
    [InlineData("placeId")]
    [InlineData("type")]
    [InlineData("name")]
    [InlineData("address")]
    [InlineData("lat")]
    [InlineData("lon")]
    public void Create_InvalidLegSnapshot_ReportsTheInvalidEndpointField(string field) {
        var leg = Leg(1, InvalidPlace(field), Point("attraction"), 1, 2);

        Assert.Contains($"legs[0].from.{field}", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false, [leg])));
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(-90, 180)]
    [InlineData(90, -180)]
    [InlineData(90, 180)]
    public void Create_StandaloneCoordinatesAtLimits_Accepted(int lat, int lon) {
        var place = Point("transport_stop") with { Latitude = lat, Longitude = lon };

        Assert.Equal(place, Assert.Single(Trip.Create("t", Zone, Day, Day.AddHours(1), false, [], stops: [place]).Stops));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("end")]
    [InlineData("timeZone")]
    public void Create_EmptyTripWithInvalidBasics_StillRejected(string field) {
        var name = field == "name" ? " " : "A plan";
        var zone = field == "timeZone" ? null! : Zone;
        var end = field == "end" ? Day.AddHours(-1) : Day.AddHours(1);

        Assert.Contains(field, RejectedFields(() => Trip.Create(name, zone, Day, end, false, [])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Create_UtcBoundsAndLocalLegOffsets_ReconstructsLegacyTripWithoutChangingOffsets(bool roundTrip) {
        var tripId = Guid.Parse("019a08e6-a150-7000-8000-000000000001");
        var hotel = Point("accommodation");
        var museum = Point("attraction");
        TripLeg[] legs = roundTrip
            ? [Leg(1, hotel, museum, 1, 2), Leg(2, museum, hotel, 3, 4)]
            : [Leg(1, hotel, museum, 1, 2)];
        var start = Day.ToUniversalTime();
        var end = Day.AddHours(10).ToUniversalTime();

        var trip = Trip.Create("Existing trip", Zone, start, end, roundTrip, legs, tripId, revision: 7);

        Assert.Equal(tripId, trip.TripId);
        Assert.Equal(7, trip.Revision);
        Assert.Equal(start, trip.Start);
        Assert.Equal(end, trip.End);
        Assert.Equal(TimeSpan.Zero, trip.Start.Offset);
        Assert.Equal(TimeSpan.Zero, trip.End.Offset);
        Assert.Equal(legs, trip.Legs);
        Assert.Equal(TimeSpan.FromHours(2), trip.Legs[0].Departure.Offset);
        Assert.Equal(TimeSpan.FromHours(2), trip.Legs[0].Arrival.Offset);
        Assert.Equal(roundTrip, trip.IsRoundTrip);
    }

    [Theory]
    [InlineData(3, 29, 2, 30, 1, false)]
    [InlineData(10, 25, 2, 30, 3, false)]
    [InlineData(10, 25, 2, 30, 1, true)]
    [InlineData(10, 25, 2, 30, 2, true)]
    public void Create_NewCompositionAtClockChange_StillRequiresValidLegOffsets(int month, int day, int hour, int minute, int offset, bool valid) {
        var time = new DateTimeOffset(2026, month, day, hour, minute, 0, TimeSpan.FromHours(offset));
        var arrival = TimeZoneInfo.ConvertTime(time.AddHours(2), Zone);
        var start = time.AddHours(-1).ToUniversalTime();
        var end = arrival.AddHours(1).ToUniversalTime();
        var leg = new TripLeg(1, Point("attraction"), Point("transport_stop"), new(Stable("train"), "train", "Train"), time, arrival);

        if (valid) Assert.Equal(time.Offset, Assert.Single(Trip.Create("Clock change", Zone, start, end, false, [leg]).Legs).Departure.Offset);
        else Assert.Contains("legs[0].departure", RejectedFields(() => Trip.Create("Clock change", Zone, start, end, false, [leg])));
    }

    [Fact]
    public void Create_CrossCatalogueIdentifierCollision_DoesNotConnectDifferentPlaceTypes() {
        var first = Point("attraction");
        var transfer = Point("transport_stop");
        var otherPlace = Point("accommodation") with { PlaceId = transfer.PlaceId };

        Assert.Contains("legs[1].from", RejectedFields(() => Trip.Create("t", Zone, Day, Day.AddHours(10), false,
            [Leg(1, first, transfer, 1, 2), Leg(2, otherPlace, first, 3, 4)])));
    }

    /**
     * Supplies one invalid catalogue snapshot value without changing the others.
     * @param field The invalid field to exercise.
     * @return A location whose selected field is outside the domain contract.
     */
    private static TripPlace InvalidPlace(string field) {
        var place = Point("transport_stop");
        return field switch {
            "placeId" => place with { PlaceId = Guid.Empty },
            "type" => place with { Type = "airport" },
            "name" => place with { Name = " " },
            "address" => place with { Address = null! },
            "lat" => place with { Latitude = 90.001m },
            _ => place with { Longitude = -180.001m }
        };
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
