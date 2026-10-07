using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Data;
/**
 * Defines snapshot reads and insertion for TransportMode records.
 */
public interface ITransportModeRepository
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Ordered records.
     */
    Task<IReadOnlyList<TransportModeRecord>> GetAllAsync(CancellationToken cancellationToken);
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    Task<TransportModeRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completed insertion.
     */
    Task AddAsync(TransportModeRecord value, CancellationToken cancellationToken);
}
