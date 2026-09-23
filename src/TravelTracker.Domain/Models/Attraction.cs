namespace TravelTracker.Domain.Models;

public sealed class Attraction
{
    public Guid AttractionId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public decimal Latitude { get; set; }

    public decimal Longitude { get; set; }

    public int EstimatedVisitMinutes { get; set; }
}
