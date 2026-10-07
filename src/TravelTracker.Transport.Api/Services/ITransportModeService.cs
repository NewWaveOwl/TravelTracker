using TravelTracker.Transport.Api.Models;
using TravelTracker.Transport.Api.Contracts;
namespace TravelTracker.Transport.Api.Services;
/**
 * Defines the standalone TransportMode application operations.
 */
public interface ITransportModeService
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
     * Validates and saves a new record.
     * @param request Writable create fields.
     * @param cancellationToken Cancels the request.
     * @return Saved record.
     */
    Task<TransportModeRecord> CreateAsync(CreateTransportModeRequest request, CancellationToken cancellationToken);
}
