using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class TransportModeService
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Ordered records.
     */
    public Task<IReadOnlyList<TransportModeRecord>> GetAllAsync(CancellationToken cancellationToken) => repository.GetAllAsync(cancellationToken);
}
