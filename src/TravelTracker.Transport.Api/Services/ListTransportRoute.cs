using TravelTracker.Transport.Api.Models;
namespace TravelTracker.Transport.Api.Services;
public sealed partial class TransportRouteService
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return Ordered records.
     */
    public Task<IReadOnlyList<TransportRouteRecord>> GetAllAsync(CancellationToken cancellationToken) => repository.GetAllAsync(cancellationToken);
}
