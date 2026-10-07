using TravelTracker.WebApi.Contracts;
using TravelTracker.WebApi.Models;

namespace TravelTracker.WebApi.Services;

/**
 * Defines creation and lookup for this resource.
 */
public interface IAttractionService
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    AttractionRecord? GetById(Guid id);

    /**
     * Creates a record from HTTP-validated input.
     * @param request Writable fields accepted by the API.
     * @return The saved record with a new GUID.
     */
    AttractionRecord Create(CreateAttractionRequest request);
}
