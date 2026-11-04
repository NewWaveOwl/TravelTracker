namespace TravelTracker.Transport.Data.Entities;

/** Stores Transport-owned values without exposing internal keys in HTTP. */
public sealed class RoutePointRow
{
    public long Id { get; set; }
    public Guid PublicId { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public Guid? SourceId { get; set; }
}
