using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Data.Repositories;

/**
 * Defines persistence operations for transportation methods.
 */
public interface ITransportModeRepository
{
    /**
     * Retrieves every transportation method.
     *
     * @return A task containing all stored transportation methods.
      * @param cancellationToken Cancels storage I/O.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    Task<List<TransportMode>> GetAllAsync(CancellationToken cancellationToken = default, string? code = null, string? name = null);

    /**
     * Retrieves a transportation method by its unique identifier.
     *
     * @param transportModeId The unique identifier to search for.
     * @return A task containing the matching transportation method, or null when it does not exist.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default);

    /**
     * Retrieves a transportation method by its stable code.
     *
     * @param code The transport code to search for, such as car or train.
     * @return A task containing the matching transportation method, or null when it does not exist.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<TransportMode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /**
     * Adds a transportation method to the data store.
     *
     * @param transportMode The transportation method to add.
     * @return A task that represents the asynchronous operation.
      * @param cancellationToken Cancels storage I/O.
     */
    Task AddAsync(TransportMode transportMode, CancellationToken cancellationToken = default);

    /**
     * Saves changes to an existing transportation method.
     *
     * @param transportMode The transportation method containing the updated values.
     * @return A task that represents the asynchronous operation.
      * @param cancellationToken Cancels storage I/O.
     */
    Task UpdateAsync(TransportMode transportMode, CancellationToken cancellationToken = default);
}
