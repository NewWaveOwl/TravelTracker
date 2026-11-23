namespace TravelTracker.Frontend.Api.Routing;

/** A route candidate whose timing basis is supplied explicitly by the backend. */
public sealed record RouteItinerary(string Id, double DurationSeconds, double DistanceMeters,
    string TimingBasis, IReadOnlyList<RoutePreviewLeg> Legs, int? TransferCount = null,
    DateTimeOffset? DepartureTime = null, DateTimeOffset? ArrivalTime = null);
