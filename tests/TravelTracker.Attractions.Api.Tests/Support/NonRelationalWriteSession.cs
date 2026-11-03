using TravelTracker.Attractions.Data.Repositories;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * Marks the absence of transactions in EF InMemory; it must never prove locking or rollback.
 */
internal sealed class NonRelationalWriteSession : IAttractionWriteSession
{
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
