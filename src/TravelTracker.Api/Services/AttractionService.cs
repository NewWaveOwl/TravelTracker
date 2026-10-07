using TravelTracker.Data;
using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Implements the existing shared service with its shared repository and entity.
 * @param repository Process-lifetime catalogue storage.
 * @param ids Issues new entity identities.
 */
public sealed class AttractionService(IAttractionRepository repository, EntityIdGenerator ids) : IAttractionService
{
    /**
     * Reads all entries through the existing contract.
     * @return A task containing the stored entries.
     */
    public Task<List<Attraction>> GetAllAsync() => repository.GetAllAsync();

    /**
     * Reads one saved entry.
     * @param attractionId Identifier to find.
     * @return A task containing the entry, or null when absent.
     */
    public Task<Attraction?> GetByIdAsync(Guid attractionId) => repository.GetByIdAsync(attractionId);

    /**
     * Creates an entity from HTTP-validated values.
     * @param attraction Writable entity values; its supplied identifier is replaced.
     * @return A task containing the stored entity with a generated identifier.
     */
    public async Task<Attraction> CreateAsync(Attraction attraction)
    {
        attraction.AttractionId = await ids.NextAsync<Attraction>();
        attraction.Name = attraction.Name.Trim();
        await repository.AddAsync(attraction);
        return attraction;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param attractionId Identifier of the entity to replace.
     * @param attraction New values supplied by the caller.
     * @return A task containing the entity, or null when absent; no HTTP update endpoint is exposed.
     */
    public async Task<Attraction?> UpdateAsync(Guid attractionId, Attraction attraction)
    {
        var existing = await repository.GetByIdAsync(attractionId);
        if (existing is null) return null;
        attraction.AttractionId = existing.AttractionId;
        attraction.Name = attraction.Name.Trim();
        await repository.UpdateAsync(attraction);
        return attraction;
    }

    /**
     * Removes an attraction through the existing contract.
     * @param attractionId Identifier to remove.
     * @return A task containing whether it was removed.
     */
    public Task<bool> DeleteAsync(Guid attractionId) => repository.DeleteAsync(attractionId);

}
