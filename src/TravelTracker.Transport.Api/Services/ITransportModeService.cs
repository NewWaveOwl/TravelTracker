using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Defines the business operations available for transportation methods.
 */
public interface ITransportModeService
{
    /**
     * Retrieves transportation methods, optionally filtered.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the matching transportation methods.
     */
    Task<List<TransportMode>> GetAllAsync(string? code = null, string? name = null, CancellationToken cancellationToken = default);

    /**
     * Retrieves a transportation method by its unique identifier.
     * @param transportModeId The unique identifier to search for.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the matching transportation method, or null when it does not exist.
     */
    Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default);

    /**
     * Creates a transportation method from HTTP-validated values.
     * @param transportMode The transportation method values to create.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the created transportation method.
     */
    Task<TransportMode> CreateAsync(TransportMode transportMode, CancellationToken cancellationToken = default);
}
