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
     * @param nameFilter Optional case-insensitive name substring applied in the database.
     * @param cancellationToken Cancels database I/O.
     * @return Detached matching domain records ordered by name, then public identifier.
     */
    Task<List<Attraction>> GetVisibleAsync(string issuer, string subject, bool includeShared, string? nameFilter = null, CancellationToken cancellationToken = default);
    /**
     * Locks the target row for a read/validate/write operation.
     * @param attractionId The public identifier to lock.
     * @param cancellationToken Cancels database work for the request.
     * @return A write session whose transaction rolls back unless explicitly committed.
     */
    Task<IAttractionWriteSession> BeginWriteAsync(Guid attractionId, CancellationToken cancellationToken = default);
    /**
     * Retrieves an attraction by its unique identifier.
     *
     * @param attractionId The unique identifier to search for.
     * @param cancellationToken Cancels database work for the request.
     * @return A task containing the matching attraction, or null when it does not exist.
     */
    Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default);

    /**
     * Adds an attraction to the data store.
     *
     * @param attraction The attraction to add.
     * @param cancellationToken Cancels database work for the request.
     * @return A task that represents the asynchronous operation.
     */
    Task AddAsync(Attraction attraction, CancellationToken cancellationToken = default);

    /**
     * Saves changes to an existing attraction.
     *
     * @param attraction The attraction containing the updated values.
     * @param cancellationToken Cancels database work for the request.
     * @return A task that represents the asynchronous operation.
     */
    Task UpdateAsync(Attraction attraction, CancellationToken cancellationToken = default);
}
