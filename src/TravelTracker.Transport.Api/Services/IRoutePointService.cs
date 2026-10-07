using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Services;
/**
 * Defines the standalone RoutePoint application operations.
 */
public interface IRoutePointService
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
     * Validates and saves a new record.
     * @param request Writable create fields.
     * @param cancellationToken Cancels the request.
     * @return Saved record.
     */
    Task<RoutePointRecord> CreateAsync(CreateRoutePointRequest request, CancellationToken cancellationToken);
}
