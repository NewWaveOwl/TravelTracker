using TravelTracker.Attractions.Api.Models;

namespace TravelTracker.Attractions.Api.Data;

/**
 * Defines the two storage operations needed by this resource.
 */
public interface IAttractionRepository
{
    /**
     * Finds a saved record.
     * @param id Server-generated record identifier.
     * @return The record, or null when absent.
     */
    AttractionRecord? GetById(Guid id);

    /**
     * Stores a newly created record.
     * @param record Record created by the service.
     */
    void Add(AttractionRecord record);
}
