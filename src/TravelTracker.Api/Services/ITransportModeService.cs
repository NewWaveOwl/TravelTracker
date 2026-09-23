using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Defines the business operations available for transportation methods.
 */
public interface ITransportModeService
{
    /**
     * Retrieves every available transportation method.
     *
     * @return A task containing all transportation methods.
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
     * @param code The transport code to search for, such as bus or pedestrian.
     * @return A task containing the matching transportation method, or null when it does not exist.
     */
    Task<TransportMode?> GetByCodeAsync(string code);

    /**
     * Validates and creates a transportation method.
     *
     * @param transportMode The transportation method values to create.
     * @return A task containing the created transportation method.
     */
    Task<TransportMode> CreateAsync(TransportMode transportMode);

    /**
     * Validates and updates a transportation method.
     *
     * @param transportModeId The unique identifier of the transportation method to update.
     * @param transportMode The new transportation method values.
     * @return A task containing the updated transportation method, or null when it does not exist.
     */
    Task<TransportMode?> UpdateAsync(
        Guid transportModeId,
        TransportMode transportMode);
}
