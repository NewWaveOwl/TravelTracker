using System.Text.Json;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Services;

/**
 * Defines the business operations available for attractions.
 */
public interface IAttractionService
{
    /**
     * Publishes an owned place or acknowledges a previously shared place.
     * @param id Existing public identifier.
     * @param cancellationToken Cancels storage work.
     * @return Whether the place is now shared.
     */
    Task<bool> ShareAsync(Guid id, CancellationToken cancellationToken);

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
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing the matching attraction, or null when it does not exist or is private to someone else.
     */
    Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default);

    /**
     * Creates an attraction from values the HTTP layer has already validated, trimming name and address.
     *
     * @param attraction The attraction values to create.
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing the created attraction.
     * @throws UnauthorizedAccessException When the caller may not create this attraction.
     */
    Task<Attraction> CreateAsync(Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Applies a partial JSON patch represented as a JsonElement to an existing attraction.
     * The JsonElement must be an object containing writable fields only. Unknown fields
     * or attempts to change the server-owned attractionId will be treated as validation errors.
     *
     * @param attractionId The identifier of the attraction to update.
     * @param patch JsonElement containing the partial update object.
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing an UpdateResult describing the outcome and any validation errors.
     */
    Task<UpdateResult> UpdatePartialAsync(Guid attractionId, JsonElement patch, CancellationToken cancellationToken = default);
}
