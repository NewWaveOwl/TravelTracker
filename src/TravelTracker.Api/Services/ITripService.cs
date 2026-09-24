using TravelTracker.Domain.Models;
using TravelTracker.Domain.Types;

namespace TravelTracker.Api.Services;

/**
 * Defines the business operations available for trips, including one-way
 * and round-trip creation, history lookup, and leg management.
 */
public interface ITripService
{
    /**
     * Retrieves a user's trip history.
     *
     * @param userId The unique identifier of the user whose trips to retrieve.
     * @return A task containing the user's trips.
     */
    Task<List<Trip>> GetHistoryAsync(Id64 userId);

    /**
     * Retrieves a trip by its unique identifier.
     *
     * @param tripId The unique identifier to search for.
     * @return A task containing the matching trip, or null when it does not exist.
     */
    Task<Trip?> GetByIdAsync(Id64 tripId);

    /**
     * Validates and creates a trip. The trip's type determines whether it is
     * built as a one-way outward leg (accommodation to attraction) or a
     * round trip that returns to the starting accommodation.
     *
     * @param trip The trip values to create.
     * @return A task containing the created trip.
     */
    Task<Trip> CreateAsync(Trip trip);

    /**
     * Validates and updates a trip.
     *
     * @param tripId The unique identifier of the trip to update.
     * @param trip The new trip values.
     * @return A task containing the updated trip, or null when it does not exist.
     */
    Task<Trip?> UpdateAsync(
        Id64 tripId,
        Trip trip);

    /**
     * Deletes a trip.
     *
     * @param tripId The unique identifier of the trip to delete.
     * @return A task containing true when the trip was deleted; otherwise, false.
     */
    Task<bool> DeleteAsync(Id64 tripId);

    /**
     * Validates and replaces the legs belonging to a trip: sequence numbers
     * must start at 1 with no gaps, arrival must follow departure on each
     * leg, and connected legs must use matching places.
     *
     * @param tripId The unique identifier of the trip whose legs to replace.
     * @param legs The full ordered set of legs to store.
     * @return A task containing the stored legs in sequence order.
     */
    Task<List<TripLeg>> ReplaceLegsAsync(
        Id64 tripId,
        List<TripLeg> legs);
}
