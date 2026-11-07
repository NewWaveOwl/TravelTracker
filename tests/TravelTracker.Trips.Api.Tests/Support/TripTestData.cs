using System.Net;
using TravelTracker.Domain.Models;

namespace TravelTracker.Trips.Api.Tests.Support;

/** Shared identifiers, accounts, catalogue scripts and trip builders for the Trips HTTP tests. */
internal static class TripTestData
{
    /** Public accommodation the trips start from. */
    internal static readonly Guid Hotel = Guid.Parse("0196a1b2-0000-7000-8000-000000000001");

    /** Public attraction the trips end at. */
    internal static readonly Guid Museum = Guid.Parse("0196a1b2-0000-7000-8000-000000000002");

    /** Public transport mode used by the legs. */
    internal static readonly Guid Train = Guid.Parse("0196a1b2-0000-7000-8000-000000000004");

    /** Public account identifiers for the test subjects. */
    internal static readonly Guid AliceId = Guid.Parse("0196a1b2-0000-7000-8000-0000000000a1");
    internal static readonly Guid BobId = Guid.Parse("0196a1b2-0000-7000-8000-0000000000b2");
    internal static readonly Guid AdminId = Guid.Parse("0196a1b2-0000-7000-8000-0000000000ad");

    /** Start of the trip in Europe/Paris summer time. */
    internal static readonly DateTimeOffset Start = new(2026, 6, 1, 8, 0, 0, TimeSpan.FromHours(2));

    /**
     * Registers the standard accounts: an active traveller, another traveller, an administrator, a banned account and a deactivated one.
     *
     * @param upstream The scripted upstream of the factory under test.
     */
    internal static void SeedAccounts(TripUpstream upstream)
    {
        upstream.Account("alice", AliceId, "active", "traveller");
        upstream.Account("bob", BobId, "active", "traveller");
        upstream.Account("admin", AdminId, "active", "administrator");
        upstream.Account("banned", Guid.Parse("0196a1b2-0000-7000-8000-0000000000e1"), "banned", "traveller");
        upstream.Account("deactivated", Guid.Parse("0196a1b2-0000-7000-8000-0000000000e2"), "deactivated", "traveller");
    }

    /**
     * Scripts the catalogue answers a healthy trip needs: the hotel, the museum and the train.
     *
     * @param upstream The scripted upstream.
     */
    internal static void SeedHealthyCatalogue(TripUpstream upstream)
    {
        upstream.Respond("GET", $"/api/v1/accommodations/{Hotel:D}", HttpStatusCode.OK,
            new { accommodationId = Hotel, name = "Hotel Lumiere", address = "1 rue de Rivoli", lat = 48.85m, lon = 2.35m, visibility = "shared" });
        upstream.Respond("GET", $"/api/v1/attractions/{Museum:D}", HttpStatusCode.OK,
            new { attractionId = Museum, name = "Louvre", address = "Rue de Rivoli", lat = 48.86m, lon = 2.34m, estimatedVisitMinutes = 120 });
        upstream.Respond("GET", $"/api/v1/transport-modes/{Train:D}", HttpStatusCode.OK,
            new { transportModeId = Train, code = "train", name = "Train" });
    }

    /**
     * Builds the JSON body of a valid one-way trip from the hotel to the museum by train.
     *
     * @param name Trip name.
     * @param timeZone Time zone id.
     * @return An anonymous object serialized as the request body.
     */
    internal static object OneWayBody(string name = "Paris weekend", string timeZone = "Europe/Paris") => new
    {
        name,
        timeZone,
        start = Start,
        end = Start.AddHours(4),
        isRoundTrip = false,
        legs = new object[]
        {
            new
            {
                sequence = 1,
                from = new { placeId = Hotel, type = "accommodation" },
                to = new { placeId = Museum, type = "attraction" },
                transportModeId = Train,
                departure = Start.AddHours(1),
                arrival = Start.AddHours(2)
            }
        }
    };

    /**
     * Stores a valid one-way trip for an owner, without going through HTTP, for detail tests.
     *
     * @param factory The host whose store receives the trip.
     * @param ownerSubject Owner subject.
     * @param ownerIssuer Owner issuer.
     * @return The stored domain trip.
     */
    internal static Trip SeedTrip(TripsApiFactory factory, string ownerSubject, string ownerIssuer = TripsApiFactory.Issuer)
    {
        var hotel = new TripPlace(Hotel, "accommodation", "Hotel Lumiere", "1 rue de Rivoli", 48.85m, 2.35m);
        var museum = new TripPlace(Museum, "attraction", "Louvre", "Rue de Rivoli", 48.86m, 2.34m);
        var trip = Trip.Create("Paris weekend", TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris"), Start, Start.AddHours(4), false,
            [new(1, hotel, museum, new(Train, "train", "Train"), Start.AddHours(1), Start.AddHours(2))]);
        factory.Repository.AddAsync(trip, ownerIssuer, ownerSubject).GetAwaiter().GetResult();
        return trip;
    }
}
