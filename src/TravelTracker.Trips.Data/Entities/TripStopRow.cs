namespace TravelTracker.Trips.Data.Entities;

/** Stores one ordered place occurrence without requiring a journey, transport mode or visit time. */
internal sealed class TripStopRow
{
    public Guid TripId { get; set; }
    public int Sequence { get; set; }
    public Guid PlaceId { get; set; }
    public string PlaceType { get; set; } = string.Empty;
    public string PlaceName { get; set; } = string.Empty;
    public string PlaceAddress { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public TripRow Trip { get; set; } = null!;
}
