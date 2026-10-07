using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;
/**
 * Defines snapshot reads and insertion for RoutePoint records.
 */
public interface IRoutePointRepository
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Ordered records.
     */
    Task<IReadOnlyList<RoutePointRecord>> GetAllAsync(CancellationToken cancellationToken);
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    Task<RoutePointRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    Task AddAsync(RoutePointRecord value, CancellationToken cancellationToken);
}
