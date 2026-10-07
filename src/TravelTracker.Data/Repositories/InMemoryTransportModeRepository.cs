using System.Collections.Concurrent;
using TravelTracker.Data.SeedData;
using TravelTracker.Domain.Models;

namespace TravelTracker.Data.Repositories;

/**
 * Implements the existing shared TransportMode repository contract in memory.
 */
public sealed class InMemoryTransportModeRepository : ITransportModeRepository
{
    private readonly ConcurrentDictionary<Guid, TransportMode> records =
        new(TransportSeedData.Transports().ToDictionary(record => record.TransportModeId));

    /**
     * Reads the catalogue through the existing repository contract.
     * @return A task containing copies of all stored entities.
     */
    public Task<List<TransportMode>> GetAllAsync() => Task.FromResult(records.Values.Select(Copy).ToList());

    /**
     * Reads one entity without exposing the stored mutable instance.
     * @param transportModeId Identifier to find.
     * @return A task containing a copy, or null when absent.
     */
    public Task<TransportMode?> GetByIdAsync(Guid transportModeId) => Task.FromResult(
        records.TryGetValue(transportModeId, out var record) ? Copy(record) : null);

    /**
     * Stores a new entity for subsequent requests.
     * @param transportMode Entity created by the service.
     * @return A completed task after storing a copy.
     */
    public Task AddAsync(TransportMode transportMode)
    {
        records[transportMode.TransportModeId] = Copy(transportMode);
        return Task.CompletedTask;
    }

    /**
     * Replaces an existing entity through the preserved contract.
     * @param transportMode New entity values with the existing identifier.
     * @return A completed task after attempting replacement; no HTTP update endpoint is exposed.
     */
    public Task UpdateAsync(TransportMode transportMode)
    {
        if (records.TryGetValue(transportMode.TransportModeId, out var previous))
            records.TryUpdate(transportMode.TransportModeId, Copy(transportMode), previous);
        return Task.CompletedTask;
    }

    /**
     * Finds the first entry with a matching stable code.
     * @param code Exact transport code.
     * @return A task containing a copy, or null when absent.
     */
    public Task<TransportMode?> GetByCodeAsync(string code) => Task.FromResult(
        records.Values.Where(record => record.Code == code).OrderBy(record => record.TransportModeId)
            .Select(Copy).FirstOrDefault());

    /**
     * Keeps callers from changing the repository's mutable shared entity.
     * @param record Entity to copy.
     * @return An independent entity with the same fields.
     */
    private static TransportMode Copy(TransportMode record) => new() { TransportModeId = record.TransportModeId, Code = record.Code, DisplayName = record.DisplayName, Description = record.Description };
}
