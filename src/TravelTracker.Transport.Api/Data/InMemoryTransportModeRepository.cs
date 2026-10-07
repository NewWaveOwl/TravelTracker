using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;
/**
 * Persists TransportMode records in the service-owned store.
 * @param store Process-lifetime thread-safe state.
 */
public sealed class InMemoryTransportModeRepository(InMemoryTransportStore store) : ITransportModeRepository
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Stable name/GUID ordered records.
     */
    public Task<IReadOnlyList<TransportModeRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TransportModeRecord> values = store.GetTransports().OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.TransportModeId).ToArray();
        return Task.FromResult(values);
    }
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    public Task<TransportModeRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.GetTransport(id));
    }
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    public Task AddAsync(TransportModeRecord value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.AddTransport(value);
        return Task.CompletedTask;
    }
}
