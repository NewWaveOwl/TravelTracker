using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;
/**
 * Persists TransportRoute records in the service-owned store.
 * @param store Process-lifetime thread-safe state.
 */
public sealed class InMemoryTransportRouteRepository(InMemoryTransportStore store) : ITransportRouteRepository
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Stable name/GUID ordered records.
     */
    public Task<IReadOnlyList<TransportRouteRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TransportRouteRecord> values = store.GetRoutes().OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.TransportRouteId).ToArray();
        return Task.FromResult(values);
    }
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    public Task<TransportRouteRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.GetRoute(id));
    }
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    public Task AddAsync(TransportRouteRecord value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.AddRoute(value);
        return Task.CompletedTask;
    }
}
