using TravelTracker.Attractions.Api.Models;
using TravelTracker.Attractions.Api.Contracts;
namespace TravelTracker.Attractions.Api.Services;

/**
 * Defines the standalone catalogue's application operations.
 */
public interface IAttractionService
{
    /**
     * Lists records in deterministic order.
     * @param cancellationToken Cancels the request.
     * @return All ordered attractions.
     */
    Task<IReadOnlyList<AttractionRecord>> GetAllAsync(CancellationToken cancellationToken);
    /**
     * Finds records using a literal name filter.
     * @param name Literal name filter.
     * @param cancellationToken Cancels the request.
     * @return Ordered matches.
     */
    Task<IReadOnlyList<AttractionRecord>> SearchAsync(string name, CancellationToken cancellationToken);
    /**
     * Looks up a record by identifier.
     * @param id Attraction identifier.
     * @param cancellationToken Cancels the request.
     * @return Record, or null.
     */
    Task<AttractionRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    /**
     * Validates and saves a new record.
     * @param request Writable create fields.
     * @param cancellationToken Cancels the request.
     * @return Saved attraction.
     */
    Task<AttractionRecord> CreateAsync(CreateAttractionRequest request, CancellationToken cancellationToken);
    /**
     * Applies supplied fields without changing omitted values.
     * @param id Attraction identifier.
     * @param request Supplied fields.
     * @param cancellationToken Cancels the request.
     * @return Saved attraction, or null.
     */
    Task<AttractionRecord?> PatchAsync(Guid id, PatchAttractionRequest request, CancellationToken cancellationToken);
}
