using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Defines the business operations available for attractions.
 */
public interface IAttractionService
{
    /**
     * Retrieves every available attraction.
     *
     * @return A task containing all attractions.
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
     * Validates and creates an attraction.
     *
     * @param attraction The attraction values to create.
     * @return A task containing the created attraction.
     */
    Task<Attraction> CreateAsync(Attraction attraction);

    /**
     * Validates and updates an attraction.
     *
     * @param attractionId The unique identifier of the attraction to update.
     * @param attraction The new attraction values.
     * @return A task containing the updated attraction, or null when it does not exist.
     */
    Task<Attraction?> UpdateAsync(
        Guid attractionId,
        Attraction attraction);

    /**
     * Deletes an attraction.
     *
     * @param attractionId The unique identifier of the attraction to delete.
     * @return A task containing true when the attraction was deleted; otherwise, false.
     */
    Task<bool> DeleteAsync(Guid attractionId);
}
