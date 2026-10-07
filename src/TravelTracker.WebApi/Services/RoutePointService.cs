using TravelTracker.WebApi.Contracts;
using TravelTracker.WebApi.Data;
using TravelTracker.WebApi.Models;

namespace TravelTracker.WebApi.Services;

/**
 * Creates and reads records through its repository.
 * @param repository Shared in-memory repository.
 */
public sealed class RoutePointService(IRoutePointRepository repository) : IRoutePointService
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public RoutePointRecord? GetById(Guid id) => repository.GetById(id);

    /**
     * Creates a record from HTTP-validated input.
     * @param request Writable fields accepted by the API.
     * @return The saved record with a new GUID.
     */
    public RoutePointRecord Create(CreateRoutePointRequest request)
    {
        RoutePointRecord record = new(Guid.NewGuid(), request.Type!, request.Name!.Trim(), request.Address ?? string.Empty, request.Lat!.Value, request.Lon!.Value, request.SourceId);
        repository.Add(record);
        return record;
    }
}
