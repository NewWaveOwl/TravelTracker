using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Defines persistence operations for trips and their child records.
 */
public interface ITripRepository
{
    /**
     * Retrieves a user's trip history.
     *
     * @param userId The public identifier of the user whose trips to retrieve.
     * @return A task containing the user's trips.
     */
    Task<List<Trip>> GetAllByUserIdAsync(Guid userId);

    /**
     * Retrieves a trip by its unique identifier.
     *
     * @param tripId The public identifier to search for.
     * @return A task containing the matching trip, or null when it does not exist.
     */
    Task<Trip?> GetByIdAsync(Guid tripId);

    /**
     * Adds a trip to the data store.
     *
     * @param trip The trip to add.
     * @return A task that represents the asynchronous operation.
     */
    Task AddAsync(Trip trip);

    /**
     * Saves changes to an existing trip.
     *
     * @param trip The trip containing the updated values.
     * @return A task that represents the asynchronous operation.
     */
    Task UpdateAsync(Trip trip);

    /**
     * Deletes a trip from the data store.
     *
     * @param tripId The public identifier of the trip to delete.
     * @return A task containing true when the trip was deleted; otherwise, false.
     */
    Task<bool> DeleteAsync(Guid tripId);

    /**
     * Replaces the legs belonging to a trip.
     *
     * @param tripId The public identifier of the trip whose legs to replace.
     * @param legs The full ordered set of legs to store.
     * @return A task that represents the asynchronous operation.
     */
    Task ReplaceLegsAsync(
        Guid tripId,
        List<TripLeg> legs);
}
