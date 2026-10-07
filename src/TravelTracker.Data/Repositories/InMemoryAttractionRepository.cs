using System.Collections.Concurrent;
using TravelTracker.Data.SeedData;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Implements the existing shared Attraction repository contract in memory.
 */
public sealed class InMemoryAttractionRepository : IAttractionRepository
{
    private readonly ConcurrentDictionary<Guid, Attraction> records =
        new(AttractionSeedData.Create().ToDictionary(record => record.AttractionId));

    /**
     * Reads the catalogue through the existing repository contract.
     * @return A task containing copies of all stored entities.
     */
    public Task<List<Attraction>> GetAllAsync() => Task.FromResult(records.Values.Select(Copy).ToList());

    /**
     * Reads one entity without exposing the stored mutable instance.
     * @param attractionId Identifier to find.
     * @return A task containing a copy, or null when absent.
     */
    public Task<Attraction?> GetByIdAsync(Guid attractionId) => Task.FromResult(
        records.TryGetValue(attractionId, out var record) ? Copy(record) : null);

    /**
     * Stores a new entity for subsequent requests.
     * @param attraction Entity created by the service.
     * @return A completed task after storing a copy.
     */
    public Task AddAsync(Attraction attraction)
    {
        records[attraction.AttractionId] = Copy(attraction);
        return Task.CompletedTask;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param attraction New entity values with the existing identifier.
     * @return A completed task after attempting replacement; no HTTP update endpoint is exposed.
     */
    public Task UpdateAsync(Attraction attraction)
    {
        // Attempt to replace the stored entity atomically. Loop until successful or the key is absent.
        while (true)
        {
            if (!records.TryGetValue(attraction.AttractionId, out var previous))
                break; // nothing to update
            // TryUpdate compares the value by reference; if another thread modified the value between
            // TryGetValue and TryUpdate, TryUpdate will fail and we retry.
            if (records.TryUpdate(attraction.AttractionId, Copy(attraction), previous))
                break;
            // Otherwise retry
        }
        return Task.CompletedTask;
    }

    /**
     * Removes a saved attraction.
     * @param attractionId Identifier to remove.
     * @return A task containing whether a record was removed.
     */
    public Task<bool> DeleteAsync(Guid attractionId) => Task.FromResult(records.TryRemove(attractionId, out _));

    /**
     * Keeps callers from changing the repository's mutable shared entity.
     * @param record Entity to copy.
     * @return An independent entity with the same fields.
     */
    private static Attraction Copy(Attraction record) => new() { AttractionId = record.AttractionId, Name = record.Name, Address = record.Address, Latitude = record.Latitude, Longitude = record.Longitude, EstimatedVisitMinutes = record.EstimatedVisitMinutes, Description = record.Description, Rating = record.Rating };
}
