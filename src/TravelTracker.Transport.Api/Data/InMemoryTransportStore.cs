using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;

/**
 * Owns independent immutable catalogues and schedules for this process.
 */
public sealed class InMemoryTransportStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, TransportModeRecord> transports = TransportSeedData.Transports().ToDictionary(value => value.TransportModeId);
    private readonly Dictionary<Guid, RoutePointRecord> points = TransportSeedData.Points().ToDictionary(value => value.RoutePointId);
    private readonly Dictionary<Guid, TransportRouteRecord> routes = new();

    /**
     * Seeds one illustrative schedule without depending on another running service.
     */
    public InMemoryTransportStore()
    {
        var departure = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var point = points[Guid.Parse("00000000-0000-0000-0000-000000000203")];
        var stops = Array.AsReadOnly(new[] { new TransportStopRecord(point, departure.AddMinutes(20), departure.AddMinutes(40), 5, true) });
        var route = new TransportRouteRecord(Guid.Parse("00000000-0000-0000-0000-000000000301"), "Example Hotel to Museum",
            transports[Guid.Parse("00000000-0000-0000-0000-000000000101")],
            points[Guid.Parse("00000000-0000-0000-0000-000000000201")],
            points[Guid.Parse("00000000-0000-0000-0000-000000000202")],
            departure, departure.AddHours(1), 10, stops);
        routes.Add(route.TransportRouteId, route);
    }

    /**
     * Reads an immutable record snapshot.
     * @return An immutable-record collection snapshot.
     */
    public IReadOnlyList<TransportModeRecord> GetTransports()
    {
        lock (gate) return transports.Values.ToArray();
    }
    /**
     * Reads an immutable record snapshot.
     * @param id Resource identifier.
     * @return The immutable record, or null when absent.
     */
    public TransportModeRecord? GetTransport(Guid id)
    {
        lock (gate) return transports.GetValueOrDefault(id);
    }
    /**
     * Stores a validated immutable record.
     * @param value Validated immutable record to insert.
     */
    public void AddTransport(TransportModeRecord value)
    {
        lock (gate) transports.Add(value.TransportModeId, value);
    }

    /**
     * Reads an immutable record snapshot.
     * @return An immutable-record collection snapshot.
     */
    public IReadOnlyList<RoutePointRecord> GetPoints()
    {
        lock (gate) return points.Values.ToArray();
    }
    /**
     * Reads an immutable record snapshot.
     * @param id Resource identifier.
     * @return The immutable record, or null when absent.
     */
    public RoutePointRecord? GetPoint(Guid id)
    {
        lock (gate) return points.GetValueOrDefault(id);
    }
    /**
     * Stores a validated immutable record.
     * @param value Validated immutable record to insert.
     */
    public void AddPoint(RoutePointRecord value)
    {
        lock (gate) points.Add(value.RoutePointId, value);
    }

    /**
     * Reads an immutable record snapshot.
     * @return An immutable-record collection snapshot.
     */
    public IReadOnlyList<TransportRouteRecord> GetRoutes()
    {
        lock (gate) return routes.Values.ToArray();
    }
    /**
     * Reads an immutable record snapshot.
     * @param id Resource identifier.
     * @return The immutable record, or null when absent.
     */
    public TransportRouteRecord? GetRoute(Guid id)
    {
        lock (gate) return routes.GetValueOrDefault(id);
    }
    /**
     * Stores a validated immutable record.
     * @param value Validated immutable record to insert.
     */
    public void AddRoute(TransportRouteRecord value)
    {
        lock (gate) routes.Add(value.TransportRouteId, value);
    }
}
