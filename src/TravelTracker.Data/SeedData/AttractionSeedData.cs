using TravelTracker.Domain.Models;

namespace TravelTracker.Data.SeedData;

/**
 * Supplies the existing fixed examples using the shared attraction entity.
 */
public static class AttractionSeedData
{
    /**
     * Creates fresh seed entities for one repository instance.
     * @return Three attractions with stable identifiers.
     */
    public static IReadOnlyList<Attraction> Create() => [
        new() { AttractionId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Montreal Museum", Description = "An example museum.", Rating = 4m,
            Address = "Montreal", Latitude = 45.4987m, Longitude = -73.5793m, EstimatedVisitMinutes = 90 },
        new() { AttractionId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Name = "Science Centre", Address = "Old Montreal", Latitude = 45.5045m,
            Longitude = -73.5516m, EstimatedVisitMinutes = 120 },
        new() { AttractionId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Old Port", Description = "A waterfront walk.", Address = "Montreal",
            Latitude = 45.507m, Longitude = -73.55m, EstimatedVisitMinutes = 60 }
    ];
}
