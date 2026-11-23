namespace TravelTracker.Trips.Domain.Models;

/**
 * Names the place types a trip leg endpoint may use, shared by the domain rules and the clients that snapshot them.
 */
public static class TripPlaceTypes
{
    /** A place owned by the Accommodations service. */
    public const string Accommodation = "accommodation";

    /** A place owned by the Attractions service. */
    public const string Attraction = "attraction";

    /** A route point owned by the Transport service. */
    public const string TransportStop = "transport_stop";
}
