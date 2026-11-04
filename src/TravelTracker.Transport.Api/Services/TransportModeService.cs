using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Implements the Transport service with its service-owned repository and entity.
 * @param repository Scoped durable catalogue storage.
 * @param ids Issues new entity identities.
 */
public sealed class TransportModeService(ITransportModeRepository repository, EntityIdGenerator ids) : ITransportModeService
{
    /**
     * Reads all entries through the existing contract.
     * @return A task containing the stored entries.
      * @param cancellationToken Cancels storage I/O.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     */
    public Task<List<TransportMode>> GetAllAsync(CancellationToken cancellationToken = default, string? code = null, string? name = null) => repository.GetAllAsync(cancellationToken, code, name);

    /**
     * Reads one saved entry.
     * @param transportModeId Identifier to find.
     * @return A task containing the entry, or null when absent.
      * @param cancellationToken Cancels storage I/O.
     */
    public Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default) => repository.GetByIdAsync(transportModeId, cancellationToken);

    /**
     * Creates an entity from HTTP-validated values.
     * @param transportMode Writable entity values; its supplied identifier is replaced.
     * @return A task containing the stored entity with a generated identifier.
      * @param cancellationToken Cancels storage I/O.
     */
    public async Task<TransportMode> CreateAsync(TransportMode transportMode, CancellationToken cancellationToken = default)
    {
        transportMode.TransportModeId = await ids.NextAsync<TransportMode>(cancellationToken);
        transportMode.DisplayName = transportMode.DisplayName.Trim();
        await repository.AddAsync(transportMode, cancellationToken);
        return transportMode;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param transportModeId Identifier of the entity to replace.
     * @param transportMode New values supplied by the caller.
     * @return A task containing the entity, or null when absent; no HTTP update endpoint is exposed.
      * @param cancellationToken Cancels storage I/O.
     */
    public async Task<TransportMode?> UpdateAsync(Guid transportModeId, TransportMode transportMode, CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdAsync(transportModeId, cancellationToken);
        if (existing is null) return null;
        transportMode.TransportModeId = existing.TransportModeId;
        transportMode.DisplayName = transportMode.DisplayName.Trim();
        await repository.UpdateAsync(transportMode, cancellationToken);
        return transportMode;
    }

    /**
     * Reads an entry by code through the existing contract.
     * @param code Exact transport code.
     * @return A task containing the entry, or null when absent.
      * @param cancellationToken Cancels storage I/O.
     */
    public Task<TransportMode?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) => repository.GetByCodeAsync(code, cancellationToken);

}
