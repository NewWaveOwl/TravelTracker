using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Services;
public sealed partial class AttractionService
{
    /**
     * Returns a deterministic catalogue view.
     * @param cancellationToken Cancels the request.
     * @return Immutable matching records in name/GUID order.
     */
    public Task<IReadOnlyList<AttractionRecord>> GetAllAsync(CancellationToken cancellationToken) => repository.SearchAsync(string.Empty, cancellationToken);
}
