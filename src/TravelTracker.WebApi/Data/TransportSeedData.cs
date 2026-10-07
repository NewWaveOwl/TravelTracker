using TravelTracker.WebApi.Models;
namespace TravelTracker.WebApi.Data;
/**
 * Provides fixed, manually entered demonstration data.
 */
public static class TransportSeedData
{
    /**
     * Creates the fixed example transport catalogue.
     * @return Named example transport entries for all six supported modes.
     */
    public static IReadOnlyList<TransportModeRecord> Transports() => [
        new(Guid.Parse("00000000-0000-0000-0000-000000000101"), "bus", "Example Bus", "A manually planned bus."),
        new(Guid.Parse("00000000-0000-0000-0000-000000000102"), "train", "Example Train", null),
        new(Guid.Parse("00000000-0000-0000-0000-000000000103"), "pedestrian", "Walking", null),
        new(Guid.Parse("00000000-0000-0000-0000-000000000104"), "bike", "Bike", "Preparation includes finding a bike."),
        new(Guid.Parse("00000000-0000-0000-0000-000000000105"), "plane", "Example Plane", null),
        new(Guid.Parse("00000000-0000-0000-0000-000000000106"), "car", "Car", null)
    ];
    /**
     * Creates the fixed example location catalogue.
     * @return Reusable accommodation, attraction and transport-stop snapshots.
     */
    public static IReadOnlyList<RoutePointRecord> Points() => [
        new(Guid.Parse("00000000-0000-0000-0000-000000000201"), "accommodation", "Example Hotel", "Montreal", 45.5m, -73.57m, null),
        new(Guid.Parse("00000000-0000-0000-0000-000000000202"), "attraction", "Montreal Museum", "Montreal", 45.4987m, -73.5793m,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new(Guid.Parse("00000000-0000-0000-0000-000000000203"), "transport_stop", "Example Station", "Montreal", 45.499m, -73.566m, null)
    ];
}
