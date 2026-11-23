using System.Net;
using System.Net.Http.Json;
using TravelTracker.Frontend.Api.Routing;
using TravelTracker.Frontend.Api.Transport;
using TravelTracker.Frontend.Api.Trips;

namespace TravelTracker.Frontend.Tests.Support;

/** Public HTTP fixtures with connected Montreal geometry and exact scheduled seconds. */
public static class RoutingFixtures
{
    public static readonly DateTimeOffset RequestedDeparture = DateTimeOffset.Parse("2026-11-14T10:00:00-05:00");
    public static readonly DateTimeOffset Departure = RequestedDeparture.AddSeconds(5);
    public static readonly DateTimeOffset Arrival = Departure.AddSeconds(452);
    public static readonly TransportDto Train = new(Guid.Parse("00000000-0000-0000-0000-000000009003"), "train", "Train", null);
    public static readonly TripPlaceDto Origin = new(Guid.Parse("00000000-0000-0000-0000-000000009001"), "accommodation", "Hotel", "Hotel street", 45.5m, -73.57m);
    public static readonly TripPlaceDto Destination = new(Guid.Parse("00000000-0000-0000-0000-000000009002"), "attraction", "Museum", "Museum street", 45.51m, -73.58m);
    public static readonly RoutePlanRequest Request = new(new(Origin.Lat, Origin.Lon), new(Destination.Lat, Destination.Lon), "train", RequestedDeparture, "America/Toronto");
    public static readonly RoutingCoverage Coverage = new("America/Toronto", new(2026, 11, 1), new(2026, 12, 31),
        [new("fixture-feed", ["RAIL"], Publisher: "Fixture transit")], ServiceRangeBasis: "global_union");
    public static readonly RoutingCapabilities Capabilities = new("success", new("OpenTripPlanner", "fixture"), Coverage,
        [new("pedestrian", "available"), new("bike", "available"), new("car", "available"), new("bus", "available"), new("train", "available")],
        "© OpenStreetMap contributors; fixture transit schedules");
    public static readonly RouteItinerary Candidate = new("scheduled-train", 452, 1300, "scheduled",
        [
            new("RAIL", 180, 650, Departure, Departure.AddSeconds(180), "_vutGnc\u0060\u0060Mg^f^", "Train A", false,
                new("Hotel", Origin.Lat, Origin.Lon, "feed:origin"), new("Exchange", 45.505m, -73.575m, "feed:exchange"),
                [new("Intermediate station", 45.503m, -73.573m, "feed:middle")]),
            new("RAIL", 212, 650, Departure.AddSeconds(240), Arrival, "guvtGvba\u0060Mg^f^", "Train B", false,
                new("Exchange", 45.505m, -73.575m, "feed:exchange"), new("Museum", Destination.Lat, Destination.Lon, "feed:destination")),
        ], 1);

    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = JsonContent.Create(value) };
    public static RoutePlanResult Success(RoutePlanRequest? request = null) => new("success", [Candidate], Coverage,
        TimeZone: request?.TimeZone ?? Request.TimeZone, RequestedDepartureTime: request?.DepartureTime ?? Request.DepartureTime);
}
