using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Data.Repositories;

/**
 * Defines persistence operations for attractions.
 */
public interface IAttractionRepository
{
    /**
     * Loads rows matching the application's explicit visibility selection.
     * @param issuer The selected owner issuer.
     * @param subject The selected owner subject.
     * @param includeShared Whether shared rows are included.
     * @param cancellationToken Cancels database I/O.
     * @return Detached matching domain records.
     */
    Task<List<Attraction>> GetVisibleAsync(string issuer, string subject, bool includeShared, CancellationToken cancellationToken = default);
    /**
     * Locks the target row for a read/validate/write operation.
     * @param attractionId The public identifier to lock.
     * @return A transaction which rolls back unless explicitly committed.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<IAttractionWriteSession> BeginWriteAsync(Guid attractionId, CancellationToken cancellationToken = default);
    /**
     * Retrieves every attraction.
     *
     * @return A task containing all stored attractions.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<List<Attraction>> GetAllAsync(CancellationToken cancellationToken = default);

    /**
     * Retrieves an attraction by its unique identifier.
     *
     * @param attractionId The unique identifier to search for.
     * @return A task containing the matching attraction, or null when it does not exist.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default);

    /**
     * Adds an attraction to the data store.
     *
     * @param attraction The attraction to add.
     * @return A task that represents the asynchronous operation.
     * @param cancellationToken Cancels database work for the request.
     */
    Task AddAsync(Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Saves changes to an existing attraction.
     *
     * @param attraction The attraction containing the updated values.
     * @return A task that represents the asynchronous operation.
     * @param cancellationToken Cancels database work for the request.
     */
    Task UpdateAsync(Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Deletes an attraction from the data store.
     *
     * @param attractionId The unique identifier of the attraction to delete.
     * @return A task containing true when the attraction was deleted; otherwise, false.
     * @param cancellationToken Cancels database work for the request.
     */
    Task<bool> DeleteAsync(Guid attractionId, CancellationToken cancellationToken = default);
}
