namespace TravelTracker.Frontend.Api.Trips;

/** The caller-writable itinerary sent to the Trips service. */
public sealed record SaveTripRequest(string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End,
    bool IsRoundTrip, IReadOnlyList<SaveTripRequest.Leg> Legs)
{
    /** A leg referencing public catalogue identifiers. */
    public sealed record Leg(int Sequence, Place From, Place To, Guid TransportModeId,
        DateTimeOffset Departure, DateTimeOffset Arrival);

    /** A place reference in the catalogue responsible for its type. */
    public sealed record Place(Guid PlaceId, string Type);
}
