using System.Collections.Concurrent;
using TravelTracker.Transport.Api.Models;

namespace TravelTracker.Transport.Api.Data;

/**
 * Keeps seeded and created records for the lifetime of this API host.
 */
public sealed class InMemoryTransportModeRepository : ITransportModeRepository
{
    private readonly ConcurrentDictionary<Guid, TransportModeRecord> records =
        new(TransportSeedData.Transports().ToDictionary(record => record.TransportModeId));

    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public TransportModeRecord? GetById(Guid id) => records.GetValueOrDefault(id);

    /**
     * Stores a record safely across simultaneous requests.
     * @param record Record with a server-generated identifier.
     */
    public void Add(TransportModeRecord record) => records[record.TransportModeId] = record;
}
