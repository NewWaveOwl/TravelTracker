namespace TravelTracker.Frontend.Api.Trips;

/** The public Trips response, including the saved itinerary snapshots. */
public sealed record TripDto(Guid TripId, string Name, string TimeZone, DateTimeOffset Start, DateTimeOffset End,
    bool IsRoundTrip, IReadOnlyList<TripDto.Leg> Legs)
{
    /** A saved leg in itinerary order. */
    public sealed record Leg(int Sequence, TripPlaceDto From, TripPlaceDto To, Mode Transport,
        DateTimeOffset Departure, DateTimeOffset Arrival);

    /** A saved transport snapshot. */
    public sealed record Mode(Guid TransportModeId, string Code, string Name);
}
