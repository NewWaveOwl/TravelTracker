using System.Collections.Concurrent;
using TravelTracker.Attractions.Api.Models;

namespace TravelTracker.Attractions.Api.Data;

/**
 * Keeps seeded and created records for the lifetime of this API host.
 */
public sealed class InMemoryAttractionRepository : IAttractionRepository
{
    private readonly ConcurrentDictionary<Guid, AttractionRecord> records =
        new(AttractionSeedData.Create().ToDictionary(record => record.AttractionId));

    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public AttractionRecord? GetById(Guid id) => records.GetValueOrDefault(id);

    /**
     * Stores a record safely across simultaneous requests.
     * @param record Record with a server-generated identifier.
     */
    public void Add(AttractionRecord record) => records[record.AttractionId] = record;
}
