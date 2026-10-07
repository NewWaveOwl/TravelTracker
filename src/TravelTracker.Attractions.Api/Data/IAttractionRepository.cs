using TravelTracker.Attractions.Api.Models;
namespace TravelTracker.Attractions.Api.Data;

/**
 * Defines the reads, insert and atomic replacement needed by the catalogue.
 */
public interface IAttractionRepository
{
    /**
     * Finds records using a literal name filter.
     * @param name Trimmed literal name filter.
     * @param cancellationToken Cancels the request.
     * @return Ordered matching records.
     */
    Task<IReadOnlyList<AttractionRecord>> SearchAsync(string name, CancellationToken cancellationToken);
    /**
     * Looks up a stored record.
     * @param id Attraction identifier.
     * @param cancellationToken Cancels the request.
     * @return The record, or null when absent.
     */
    Task<AttractionRecord?> GetAsync(Guid id, CancellationToken cancellationToken);
    /**
     * Stores an already validated record.
     * @param value Validated record.
     * @param cancellationToken Cancels the request.
     * @return Completion after insertion.
     */
    Task AddAsync(AttractionRecord value, CancellationToken cancellationToken);
    /**
     * Replaces a record through an atomic application operation.
     * @param id Attraction identifier.
     * @param replace Application candidate operation.
     * @param cancellationToken Cancels the request.
     * @return Saved replacement, or null.
     */
    Task<AttractionRecord?> UpdateAsync(Guid id, Func<AttractionRecord, AttractionRecord> replace, CancellationToken cancellationToken);
}
