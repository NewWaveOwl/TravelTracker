namespace TravelTracker.Trips.Data.Entities;

/**
 * Stores one ordered leg with its place and transport snapshots. The snapshot columns are copied at save time,
 * so catalogue edits never change a stored trip.
 */
internal sealed class TripLegRow
{
    public Guid TripId { get; set; }
    public int Sequence { get; set; }
    public Guid FromPlaceId { get; set; }
    public string FromPlaceType { get; set; } = string.Empty;
    public string FromPlaceName { get; set; } = string.Empty;
    public string FromPlaceAddress { get; set; } = string.Empty;
    public decimal FromLatitude { get; set; }
    public decimal FromLongitude { get; set; }
    public Guid ToPlaceId { get; set; }
    public string ToPlaceType { get; set; } = string.Empty;
    public string ToPlaceName { get; set; } = string.Empty;
    public string ToPlaceAddress { get; set; } = string.Empty;
    public decimal ToLatitude { get; set; }
    public decimal ToLongitude { get; set; }
    public Guid TransportModeId { get; set; }
    public string TransportCode { get; set; } = string.Empty;
    public string TransportName { get; set; } = string.Empty;
    public DateTimeOffset DepartureAt { get; set; }
    public TimeSpan DepartureOffset { get; set; }
    public DateTimeOffset ArrivalAt { get; set; }
    public TimeSpan ArrivalOffset { get; set; }
    public TripRow Trip { get; set; } = null!;
}
