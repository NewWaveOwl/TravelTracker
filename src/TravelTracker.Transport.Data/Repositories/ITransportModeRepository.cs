using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Defines persistence operations for transportation methods.
 */
public interface ITransportModeRepository
{
    /**
     * Retrieves transportation methods, optionally filtered.
     * @param cancellationToken Cancels storage I/O.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @return A task containing the matching stored transportation methods.
     */
    Task<List<TransportMode>> GetAllAsync(CancellationToken cancellationToken = default, string? code = null, string? name = null);

    /**
     * Retrieves a transportation method by its unique identifier.
     * @param transportModeId The unique identifier to search for.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the matching transportation method, or null when it does not exist.
     */
    Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default);

    /**
     * Adds a transportation method to the data store.
     * @param transportMode The transportation method to add.
     * @param cancellationToken Cancels storage I/O.
     * @return A task that represents the asynchronous operation.
     */
    Task AddAsync(TransportMode transportMode, CancellationToken cancellationToken = default);
}
