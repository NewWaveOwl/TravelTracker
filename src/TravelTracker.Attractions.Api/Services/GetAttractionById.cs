using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Services;
public sealed partial class AttractionService
{
    /**
     * Looks up a record by identifier.
     * @param id Attraction identifier.
     * @param cancellationToken Cancels the lookup.
     * @return Record, or null when absent.
     */
    public Task<AttractionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        repository.GetAsync(id, cancellationToken);
}
