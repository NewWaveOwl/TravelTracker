using Microsoft.EntityFrameworkCore.Storage;

namespace TravelTracker.Attractions.Data.Repositories;

/**
 * Adapts a database transaction without exposing EF to application services.
 * @param transaction The row-locking transaction.
 */
internal sealed class AttractionWriteSession(IDbContextTransaction transaction) : IAttractionWriteSession
{
    /**
     * Commits the accepted candidate.
     * @param cancellationToken Cancels the commit.
     * @return Completion after committing.
     */
    public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
    /**
     * Releases the lock and rolls back any uncommitted work.
     * @return Completion after disposing the transaction.
     */
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
