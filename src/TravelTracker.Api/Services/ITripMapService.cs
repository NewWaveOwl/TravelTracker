using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Defines conversion of a trip into the ordered points required by the
 * global map, keeping map formatting logic outside the controller.
 */
public interface ITripMapService
{
    /**
     * Retrieves the ordered map points for a trip, each carrying its place
     * ID and type, sequence and scheduled time, and the transport-mode ID
     * for the segment leading to it.
     *
     * @param tripId The public identifier of the trip to map.
     * @return A task containing the trip's map points in order.
     */
    Task<List<TripMapPoint>> GetMapPointsAsync(Guid tripId);
}
