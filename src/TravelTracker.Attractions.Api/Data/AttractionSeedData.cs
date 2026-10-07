using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Data;

/**
 * Supplies fixed example records for independent local demonstrations.
 */
public static class AttractionSeedData
{
    /**
     * Creates a new immutable seed collection.
     * @return Three attractions with stable identifiers.
     */
    public static IReadOnlyList<AttractionRecord> Create() => [
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Montreal Museum",
            "An example museum.", 4m, "Montreal", 45.4987m, -73.5793m, 90),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Science Centre",
            null, null, "Old Montreal", 45.5045m, -73.5516m, 120),
        new(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Old Port",
            "A waterfront walk.", null, "Montreal", 45.507m, -73.55m, 60)
    ];
}
