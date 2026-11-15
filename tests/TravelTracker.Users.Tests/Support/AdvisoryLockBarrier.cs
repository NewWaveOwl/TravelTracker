using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelTracker.Users.Tests.Support;

/**
 * holds each provisioning transaction at its advisory lock until every participant has reached it, so the lock really contends.
 * a SaveChanges barrier cannot be used here: the lock serializes writers before SaveChanges, so the second writer never gets there.
 * @param participants how many transactions must reach the lock before any takes it.
 */
internal sealed class AdvisoryLockBarrier(int participants) : DbCommandInterceptor
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;

    /** whether every participant reached the lock together. */
    public bool Completed => released.Task.IsCompletedSuccessfully;

    /**
     * waits at the provisioning lock statement until the other participants arrive.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken unused; the barrier has its own timeout.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref arrivals) == participants) released.TrySetResult();
            await released.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        return result;
    }
}
