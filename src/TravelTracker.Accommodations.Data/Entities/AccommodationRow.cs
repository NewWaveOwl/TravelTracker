namespace TravelTracker.Accommodations.Data.Entities;

/** Stores the accommodation values and server-resolved ownership in PostgreSQL. */
internal sealed class AccommodationRow
{
    public Guid AccommodationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string OwnerIssuer { get; set; } = string.Empty;
    public string OwnerSubject { get; set; } = string.Empty;
    public bool IsShared { get; set; }
}
