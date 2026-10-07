using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Services;
public sealed partial class AttractionService
{
    /**
     * Returns a deterministic catalogue view.
     * @param name Literal name substring; surrounding spaces are ignored.
     * @param cancellationToken Cancels the request.
     * @return Immutable matching records in name/GUID order.
     */
    public Task<IReadOnlyList<AttractionRecord>> SearchAsync(string name, CancellationToken cancellationToken) => repository.SearchAsync(name.Trim(), cancellationToken);
}
