using TravelTracker.Attractions.Data;
using TravelTracker.Attractions.Data.Repositories;
using TravelTracker.Attractions.Domain.Models;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Uses production EF reads/writes on InMemory for application tests only, without relational guarantees.
 * @param database The isolated EF InMemory context.
 */
internal sealed class InMemoryEfAttractionRepository(AttractionsDbContext database) : IAttractionRepository
{
    private readonly EfAttractionRepository repository = new(database);
    public Task<List<Attraction>> GetVisibleAsync(string issuer, string subject, bool includeShared, CancellationToken cancellationToken = default) =>
        repository.GetVisibleAsync(issuer, subject, includeShared, cancellationToken);
    public Task<List<Attraction>> GetAllAsync(CancellationToken cancellationToken = default) => repository.GetAllAsync(cancellationToken);
    public Task<Attraction?> GetByIdAsync(Guid attractionId, CancellationToken cancellationToken = default) => repository.GetByIdAsync(attractionId, cancellationToken);
    public Task AddAsync(Attraction attraction, CancellationToken cancellationToken = default) => repository.AddAsync(attraction, cancellationToken);
    public Task UpdateAsync(Attraction attraction, CancellationToken cancellationToken = default) => repository.UpdateAsync(attraction, cancellationToken);

    /**
     * Deliberately provides no lock/transaction; PostgreSQL tests alone prove write serialization.
     * @param attractionId The requested public identifier.
     * @param cancellationToken Cancels the operation.
     * @return A no-op session for sequential application tests.
     */
    public Task<IAttractionWriteSession> BeginWriteAsync(Guid attractionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IAttractionWriteSession>(new NonRelationalWriteSession());
    }

    /**
     * Prevents accidentally citing the relational-only deletion path as in-memory evidence.
     * @param attractionId The requested identifier.
     * @param cancellationToken Cancels the operation.
     * @return No result; DELETE is not an exposed HTTP operation and requires relational tests.
     */
    public Task<bool> DeleteAsync(Guid attractionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Use PostgreSQL for relational delete behavior.");
}
