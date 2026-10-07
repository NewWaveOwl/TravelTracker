using TravelTracker.Data.Repositories;
using TravelTracker.Domain.Models;

namespace TravelTracker.Api.Services;

/**
 * Implements the existing shared service with its shared repository and entity.
 * @param repository Process-lifetime catalogue storage.
 */
public sealed class TransportModeService(ITransportModeRepository repository) : ITransportModeService
{
    /**
     * Reads all entries through the existing contract.
     * @return A task containing the stored entries.
     */
    public Task<List<TransportMode>> GetAllAsync() => repository.GetAllAsync();

    /**
     * Reads one saved entry.
     * @param transportModeId Identifier to find.
     * @return A task containing the entry, or null when absent.
     */
    public Task<TransportMode?> GetByIdAsync(Guid transportModeId) => repository.GetByIdAsync(transportModeId);

    /**
     * Creates an entity from HTTP-validated values.
     * @param transportMode Writable entity values; its supplied identifier is replaced.
     * @return A task containing the stored entity with a generated identifier.
     */
    public async Task<TransportMode> CreateAsync(TransportMode transportMode)
    {
        transportMode.TransportModeId = Guid.NewGuid();
        transportMode.DisplayName = transportMode.DisplayName.Trim();
        await repository.AddAsync(transportMode);
        return transportMode;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param transportModeId Identifier of the entity to replace.
     * @param transportMode New values supplied by the caller.
     * @return A task containing the entity, or null when absent; no HTTP update endpoint is exposed.
     */
    public async Task<TransportMode?> UpdateAsync(Guid transportModeId, TransportMode transportMode)
    {
        if (await repository.GetByIdAsync(transportModeId) is null) return null;
        transportMode.TransportModeId = transportModeId;
        transportMode.DisplayName = transportMode.DisplayName.Trim();
        await repository.UpdateAsync(transportMode);
        return transportMode;
    }

    /**
     * Reads an entry by code through the existing contract.
     * @param code Exact transport code.
     * @return A task containing the entry, or null when absent.
     */
    public Task<TransportMode?> GetByCodeAsync(string code) => repository.GetByCodeAsync(code);

}
