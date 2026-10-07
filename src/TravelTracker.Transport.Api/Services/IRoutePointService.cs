using TravelTracker.Transport.Api.Contracts;
using TravelTracker.Transport.Api.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Defines creation and lookup for this resource.
 */
public interface IRoutePointService
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    RoutePointRecord? GetById(Guid id);

    /**
     * Creates a record from HTTP-validated input.
     * @param request Writable fields accepted by the API.
     * @return The saved record with a new GUID.
     */
    RoutePointRecord Create(CreateRoutePointRequest request);
}
