using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Data;

/**
 * Owns immutable process-lifetime records and serializes compound updates.
 */
public sealed class InMemoryAttractionStore
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, AttractionRecord> records =
        AttractionSeedData.Create().ToDictionary(value => value.AttractionId);

    /**
     * Takes a collection snapshot safe to enumerate outside the lock.
     * @return The current immutable records.
     */
    public IReadOnlyList<AttractionRecord> Snapshot()
    {
        lock (gate) return records.Values.ToArray();
    }

    /**
     * Looks up an immutable record.
     * @param id The attraction identifier.
     * @return The attraction, or null when absent.
     */
    public AttractionRecord? Get(Guid id)
    {
        lock (gate) return records.GetValueOrDefault(id);
    }

    /**
     * Inserts an already validated immutable record.
     * @param value The new attraction.
     */
    public void Add(AttractionRecord value)
    {
        lock (gate) records.Add(value.AttractionId, value);
    }

    /**
     * Applies and validates a replacement while protecting the original snapshot.
     * @param id The attraction identifier.
     * @param replace The application's candidate construction and validation operation.
     * @return The replacement, or null when absent; exceptions leave the old record intact.
     */
    public AttractionRecord? Update(Guid id, Func<AttractionRecord, AttractionRecord> replace)
    {
        lock (gate)
        {
            if (!records.TryGetValue(id, out var current)) return null;
            var candidate = replace(current);
            records[id] = candidate;
            return candidate;
        }
    }
}
