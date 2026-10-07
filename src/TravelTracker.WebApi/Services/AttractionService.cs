using TravelTracker.WebApi.Contracts;
using TravelTracker.WebApi.Data;
using TravelTracker.WebApi.Models;

namespace TravelTracker.WebApi.Services;

/**
 * Creates and reads records through its repository.
 * @param repository Shared in-memory repository.
 */
public sealed class AttractionService(IAttractionRepository repository) : IAttractionService
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    public AttractionRecord? GetById(Guid id) => repository.GetById(id);

    /**
     * Creates a record from HTTP-validated input.
     * @param request Writable fields accepted by the API.
     * @return The saved record with a new GUID.
     */
    public AttractionRecord Create(CreateAttractionRequest request)
    {
        AttractionRecord record = new(Guid.NewGuid(), request.Name!.Trim(), request.Description, request.Rating, request.Address ?? string.Empty, request.Lat!.Value, request.Lon!.Value, request.EstimatedVisitMinutes!.Value);
        repository.Add(record);
        return record;
    }
}
