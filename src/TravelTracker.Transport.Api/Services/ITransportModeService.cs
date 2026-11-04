using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Defines the business operations available for transportation methods.
 */
public interface ITransportModeService
{
    /**
     * Retrieves every available transportation method.
     *
     * @return A task containing all transportation methods.
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
     * @param code The transport code to search for, such as bus or pedestrian.
     * @return A task containing the matching transportation method, or null when it does not exist.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<TransportMode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /**
     * Validates and creates a transportation method.
     *
     * @param transportMode The transportation method values to create.
     * @return A task containing the created transportation method.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<TransportMode> CreateAsync(TransportMode transportMode, CancellationToken cancellationToken = default);

    /**
     * Validates and updates a transportation method.
     *
     * @param transportModeId The unique identifier of the transportation method to update.
     * @param transportMode The new transportation method values.
     * @return A task containing the updated transportation method, or null when it does not exist.
      * @param cancellationToken Cancels storage I/O.
     */
    Task<TransportMode?> UpdateAsync(
        Guid transportModeId,
        TransportMode transportMode, CancellationToken cancellationToken = default);
}
