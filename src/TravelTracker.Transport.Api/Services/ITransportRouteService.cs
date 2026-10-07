using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Services;
/**
 * Defines the standalone TransportRoute application operations.
 */
public interface ITransportRouteService
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Ordered records.
     */
    Task<IReadOnlyList<TransportRouteRecord>> GetAllAsync(CancellationToken cancellationToken);
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    Task<TransportRouteRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /**
     * Validates and saves a new record.
     * @param request Writable create fields.
     * @param cancellationToken Cancels the request.
     * @return Saved record.
     */
    Task<TransportRouteRecord> CreateAsync(CreateTransportRouteRequest request, CancellationToken cancellationToken);
}
