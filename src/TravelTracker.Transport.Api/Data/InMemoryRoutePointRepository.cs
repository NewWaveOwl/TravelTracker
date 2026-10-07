using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;
/**
 * Persists RoutePoint records in the service-owned store.
 * @param store Process-lifetime thread-safe state.
 */
public sealed class InMemoryRoutePointRepository(InMemoryTransportStore store) : IRoutePointRepository
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Stable name/GUID ordered records.
     */
    public Task<IReadOnlyList<RoutePointRecord>> GetAllAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<RoutePointRecord> values = store.GetPoints().OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.RoutePointId).ToArray();
        return Task.FromResult(values);
    }
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    public Task<RoutePointRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(store.GetPoint(id));
    }
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    public Task AddAsync(RoutePointRecord value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.AddPoint(value);
        return Task.CompletedTask;
    }
}
