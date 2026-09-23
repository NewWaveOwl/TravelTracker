using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Defines persistence operations for transportation methods.
 */
public interface ITransportModeRepository
{
    /**
     * Retrieves every transportation method.
     *
     * @return A task containing all stored transportation methods.
     */
    Task<List<TransportMode>> GetAllAsync();

    /**
     * Retrieves a transportation method by its unique identifier.
     *
     * @param transportModeId The unique identifier to search for.
     * @return A task containing the matching transportation method, or null when it does not exist.
     */
    Task<TransportMode?> GetByIdAsync(Guid transportModeId);

    /**
     * Retrieves a transportation method by its stable code.
     *
     * @param code The transport code to search for, such as car or train.
     * @return A task containing the matching transportation method, or null when it does not exist.
     */
    Task<TransportMode?> GetByCodeAsync(string code);

    /**
     * Adds a transportation method to the data store.
     *
     * @param transportMode The transportation method to add.
     * @return A task that represents the asynchronous operation.
     */
    Task AddAsync(TransportMode transportMode);

    /**
     * Saves changes to an existing transportation method.
     *
     * @param transportMode The transportation method containing the updated values.
     * @return A task that represents the asynchronous operation.
     */
    Task UpdateAsync(TransportMode transportMode);
}
