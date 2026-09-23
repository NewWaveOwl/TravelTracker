using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Defines persistence operations for attractions.
 */
public interface IAttractionRepository
{
    /**
     * Retrieves every attraction.
     *
     * @return A task containing all stored attractions.
     */
    Task<List<Attraction>> GetAllAsync();

    /**
     * Retrieves an attraction by its unique identifier.
     *
     * @param attractionId The unique identifier to search for.
     * @return A task containing the matching attraction, or null when it does not exist.
     */
    Task<Attraction?> GetByIdAsync(Guid attractionId);

    /**
     * Adds an attraction to the data store.
     *
     * @param attraction The attraction to add.
     * @return A task that represents the asynchronous operation.
     */
    Task AddAsync(Attraction attraction);

    /**
     * Saves changes to an existing attraction.
     *
     * @param attraction The attraction containing the updated values.
     * @return A task that represents the asynchronous operation.
     */
    Task UpdateAsync(Attraction attraction);

    /**
     * Deletes an attraction from the data store.
     *
     * @param attractionId The unique identifier of the attraction to delete.
     * @return A task containing true when the attraction was deleted; otherwise, false.
     */
    Task<bool> DeleteAsync(Guid attractionId);
}
