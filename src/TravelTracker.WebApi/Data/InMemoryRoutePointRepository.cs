using System.Collections.Concurrent;
using TravelTracker.WebApi.Models;

namespace TravelTracker.WebApi.Data;

/**
 * Keeps seeded and created records for the lifetime of this API host.
 */
public sealed class InMemoryRoutePointRepository : IRoutePointRepository
{
    private readonly ConcurrentDictionary<Guid, RoutePointRecord> records =
        new(TransportSeedData.Points().ToDictionary(record => record.RoutePointId));

    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public RoutePointRecord? GetById(Guid id) => records.GetValueOrDefault(id);

    /**
     * Stores a record safely across simultaneous requests.
     * @param record Record with a server-generated identifier.
     */
    public void Add(RoutePointRecord record) => records[record.RoutePointId] = record;
}
