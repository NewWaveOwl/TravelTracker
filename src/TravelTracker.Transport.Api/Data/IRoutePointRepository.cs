using TravelTracker.Transport.Api.Models;

namespace TravelTracker.Transport.Api.Data;

/**
 * Defines the two storage operations needed by this resource.
 */
public interface IRoutePointRepository
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    RoutePointRecord? GetById(Guid id);

    /**
     * Stores a newly created record.
     * @param record Record created by the service.
     */
    void Add(RoutePointRecord record);
}
