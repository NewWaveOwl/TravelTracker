using TravelTracker.WebApi.Contracts;
using TravelTracker.WebApi.Data;
using TravelTracker.WebApi.Models;

namespace TravelTracker.WebApi.Services;

/**
 * Creates and reads records through its repository.
 * @param repository Shared in-memory repository.
 */
public sealed class TransportModeService(ITransportModeRepository repository) : ITransportModeService
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public TransportModeRecord? GetById(Guid id) => repository.GetById(id);

    /**
     * Creates a record from HTTP-validated input.
     * @param request Writable fields accepted by the API.
     * @return The saved record with a new GUID.
     */
    public TransportModeRecord Create(CreateTransportModeRequest request)
    {
        TransportModeRecord record = new(Guid.NewGuid(), request.Code!, request.Name!.Trim(), request.Description);
        repository.Add(record);
        return record;
    }
}
