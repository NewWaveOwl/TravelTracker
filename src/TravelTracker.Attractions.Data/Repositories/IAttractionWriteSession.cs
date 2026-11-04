namespace TravelTracker.Attractions.Data.Repositories;

/**

 * Holds a single row's transactional write boundary until validation completes.

 */
public interface IAttractionWriteSession : IAsyncDisposable
{
    /**
     * Commits a fully validated write; disposal without this call rolls it back.
     * @param cancellationToken Cancels the commit.
     * @return Completion after committing the transaction.
     */
    Task CommitAsync(CancellationToken cancellationToken = default);
}
