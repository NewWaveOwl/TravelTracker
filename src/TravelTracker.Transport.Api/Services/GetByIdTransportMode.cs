using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class TransportModeService
{
    /**
     * Looks up a record by identifier.
     * @param id Resource identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null when absent.
     */
    public Task<TransportModeRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => repository.GetByIdAsync(id, cancellationToken);
}
