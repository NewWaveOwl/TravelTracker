namespace TravelTracker.Trips.Data.Entities;

/**
 * Stores one trip's own values and server-resolved owner. Offsets are kept beside the instants because
 * PostgreSQL timestamptz does not keep the offset the traveller entered.
 */
internal sealed class TripRow
{
    public Guid TripId { get; set; }
    public string OwnerIssuer { get; set; } = string.Empty;
    public string OwnerSubject { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTimeOffset StartAt { get; set; }
    public TimeSpan StartOffset { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public TimeSpan EndOffset { get; set; }
    public bool IsRoundTrip { get; set; }
    public List<TripLegRow> Legs { get; set; } = [];
}
