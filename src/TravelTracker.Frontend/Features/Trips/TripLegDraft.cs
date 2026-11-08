namespace TravelTracker.Frontend.Features.Trips;

/** One itinerary leg as typed so far; times are local to the trip time zone, exactly as entered. */
public sealed class TripLegDraft
{
    /** Where the journey starts. */
    public TripPlaceChoice? From { get; set; }

    /** Where the journey ends. */
    public TripPlaceChoice? To { get; set; }

    /** Public identifier of the chosen transport mode. */
    public Guid? TransportModeId { get; set; }

    /** Display name of the chosen transport mode. */
    public string TransportName { get; set; } = "";

    /** Local departure as a datetime-local value, such as 2026-06-01T09:00. */
    public string Departure { get; set; } = "";

    /** Local arrival as a datetime-local value. */
    public string Arrival { get; set; } = "";
}
