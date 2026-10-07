using TravelTracker.Domain.Models;
using TravelTracker.Domain.Types;

namespace TravelTracker.Data.SeedData;

/**
 * Supplies fresh, manually entered transport and point examples.
 */
public static class TransportSeedData
{
    /**
     * Creates the fixed transport catalogue using the shared entity.
     * @return Six named transport entries with stable identifiers.
     */
    public static IReadOnlyList<TransportMode> Transports() => [
        new() { TransportModeId = TransportId(1, "00000000-0000-0000-0000-000000000101"), Code = "bus", DisplayName = "Example Bus", Description = "A manually planned bus." },
        new() { TransportModeId = TransportId(2, "00000000-0000-0000-0000-000000000102"), Code = "train", DisplayName = "Example Train" },
        new() { TransportModeId = TransportId(3, "00000000-0000-0000-0000-000000000103"), Code = "pedestrian", DisplayName = "Walking" },
        new() { TransportModeId = TransportId(4, "00000000-0000-0000-0000-000000000104"), Code = "bike", DisplayName = "Bike", Description = "Preparation includes finding a bike." },
        new() { TransportModeId = TransportId(5, "00000000-0000-0000-0000-000000000105"), Code = "plane", DisplayName = "Example Plane" },
        new() { TransportModeId = TransportId(6, "00000000-0000-0000-0000-000000000106"), Code = "car", DisplayName = "Car" }
    ];

    /**
     * Creates reusable locations without schedule times.
     * @return Accommodation, attraction and transport-stop examples.
     */
    public static IReadOnlyList<RoutePoint> Points() => [
        new(PointId(1, "00000000-0000-0000-0000-000000000201"), "accommodation", "Example Hotel", "Montreal", 45.5m, -73.57m, null),
        new(PointId(2, "00000000-0000-0000-0000-000000000202"), "attraction", "Montreal Museum", "Montreal", 45.4987m, -73.5793m,
            Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new(PointId(3, "00000000-0000-0000-0000-000000000203"), "transport_stop", "Example Station", "Montreal", 45.499m, -73.566m, null)
    ];

    private static EntityId<TransportMode> TransportId(long key, string publicId) => new(new Id64(key), Guid.Parse(publicId));

    private static EntityId<RoutePoint> PointId(long key, string publicId) => new(new Id64(key), Guid.Parse(publicId));
}
