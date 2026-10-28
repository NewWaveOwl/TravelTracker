using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Services;

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
    /**
     * Retrieves every available attraction, optionally filtered by name.
     *
     * @param nameFilter Optional substring to match against attraction names (case-insensitive).
     * @param cancellationToken Cancels database work.
     * @return A task containing the matching attractions, deterministically ordered.
     */
    Task<List<Attraction>> GetAllAsync(string? nameFilter = null, CancellationToken cancellationToken = default);

    /**
     * Retrieves an attraction by its unique identifier.
     *
     * @param attractionId The unique identifier to search for.
     * @return A task containing the matching attraction, or null when it does not exist.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default);

    /**
     * Validates and creates an attraction.
     *
     * @param attraction The attraction values to create.
     * @return A task containing the created attraction.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<Attraction> CreateAsync(Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Validates and updates an attraction.
     *
     * @param attractionId The unique identifier of the attraction to update.
     * @param attraction The new attraction values.
     * @return A task containing the updated attraction, or null when it does not exist.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<Attraction?> UpdateAsync(
        Guid attractionId,
        Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Applies a partial JSON patch represented as a JsonElement to an existing attraction.
     * The JsonElement must be an object containing writable fields only. Unknown fields
     * or attempts to change the server-owned attractionId will be treated as validation errors.
     *
     * @param attractionId The identifier of the attraction to update.
     * @param patch JsonElement containing the partial update object.
     * @return A task containing an UpdateResult describing the outcome and any validation errors.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<UpdateResult> UpdatePartialAsync(Guid attractionId, System.Text.Json.JsonElement patch, CancellationToken cancellationToken = default);

    /**
     * Deletes an attraction.
     *
     * @param attractionId The unique identifier of the attraction to delete.
     * @return A task containing true when the attraction was deleted; otherwise, false.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<bool> DeleteAsync(Guid attractionId, CancellationToken cancellationToken = default);
}
