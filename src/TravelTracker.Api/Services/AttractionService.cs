using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Implements the existing shared service with its shared repository and entity.
 * @param repository Process-lifetime catalogue storage.
 */
public sealed class AttractionService(IAttractionRepository repository) : IAttractionService
{
    /**
     * Reads all entries through the existing contract.
     * @return A task containing the stored entries.
     */
    public async Task<List<Attraction>> GetAllAsync(string? nameFilter = null)
    {
        var records = await repository.GetAllAsync();
        IEnumerable<Attraction> filtered = records;
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var q = nameFilter.Trim();
            filtered = filtered.Where(r => r.Name != null && r.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        return filtered.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

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
        attraction.AttractionId = Guid.NewGuid();
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
        if (await repository.GetByIdAsync(attractionId) is null) return null;
        attraction.AttractionId = attractionId;
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
