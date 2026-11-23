using TravelTracker.Transport.Data;
using TravelTracker.Transport.Data.Repositories;
using TravelTracker.Transport.Domain.Models;

namespace TravelTracker.Transport.Api.Services;

/**
 * Implements the Transport service with its service-owned repository and entity.
 * @param repository Scoped durable catalogue storage.
 * @param identityGenerator Issues new entity identities.
 */
public sealed class TransportModeService(ITransportModeRepository repository, EntityIdGenerator identityGenerator) : ITransportModeService
{
    /**
     * Reads all entries through the existing contract.
     * @param code Optional exact category filter.
     * @param name Optional ordinal case-insensitive name substring.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the stored entries.
     */
    public Task<List<TransportMode>> GetAllAsync(string? code = null, string? name = null, CancellationToken cancellationToken = default) => repository.GetAllAsync(cancellationToken, code, name);

    /**
     * Reads one saved entry.
     * @param transportModeId Identifier to find.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing the entry, or null when absent.
     */
    public Task<TransportMode?> GetByIdAsync(Guid transportModeId, CancellationToken cancellationToken = default) => repository.GetByIdAsync(transportModeId, cancellationToken);

    /**
     * Generates identity and trims the name before saving HTTP-validated values.
     * @param transportMode Writable entity values; its supplied identifier is replaced.
     * @param cancellationToken Cancels storage I/O.
     * @return A task containing a new entity with a generated identifier.
     */
    public async Task<TransportMode> CreateAsync(TransportMode transportMode, CancellationToken cancellationToken = default)
    {
        var saved = transportMode with { TransportModeId = await identityGenerator.NextAsync<TransportMode>(cancellationToken), DisplayName = transportMode.DisplayName.Trim() };
        await repository.AddAsync(saved, cancellationToken);
        return saved;
    }
}
