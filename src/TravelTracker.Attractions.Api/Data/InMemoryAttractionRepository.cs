using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Data;

/**
 * Implements catalogue persistence over the process-lifetime store.
 * @param store Shared thread-safe state.
 */
public sealed class InMemoryAttractionRepository(InMemoryAttractionStore store) : IAttractionRepository
{
    /**
     * Finds records using a literal name filter.
     * @param name Literal name filter.
     * @param cancellationToken Cancels the request.
     * @return Stable name/GUID ordered results.
     */
    public Task<IReadOnlyList<AttractionRecord>> SearchAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AttractionRecord> values = store.Snapshot()
            .Where(value => value.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.AttractionId).ToArray();
        return Task.FromResult(values);
    }
    /**
     * Looks up a stored record.
     * @param id Attraction identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    public Task<AttractionRecord?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.Get(id));
    }
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    public Task AddAsync(AttractionRecord value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.Add(value);
        return Task.CompletedTask;
    }
    /**
     * Replaces a record through an atomic application operation.
     * @param id Attraction identifier.
     * @param replace Application candidate operation.
     * @param cancellationToken Cancels the request.
     * @return Saved replacement, or null.
     */
    public Task<AttractionRecord?> UpdateAsync(Guid id, Func<AttractionRecord, AttractionRecord> replace, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.Update(id, replace));
    }
}
