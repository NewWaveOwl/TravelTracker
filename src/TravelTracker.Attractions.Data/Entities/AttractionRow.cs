namespace TravelTracker.Attractions.Data.Entities;

/**

 * Stores persistence-only identity and ownership alongside attraction values.

 */
internal sealed class AttractionRow
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public decimal? Rating { get; set; }
    public string? Description { get; set; }
    public int EstimatedVisitMinutes { get; set; }
    public string OwnerIssuer { get; set; } = "";
    public string OwnerSubject { get; set; } = "";
    public bool IsShared { get; set; }
}
