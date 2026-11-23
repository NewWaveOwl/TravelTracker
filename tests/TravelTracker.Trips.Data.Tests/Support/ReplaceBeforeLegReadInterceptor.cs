using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelTracker.Trips.Data.Tests.Support;

/** Commits a replacement immediately before a reader fetches the trip's legs. */
internal sealed class ReplaceBeforeLegReadInterceptor(Func<CancellationToken, Task> replace) : DbCommandInterceptor
{
    internal bool ReplacementCommitted { get; private set; }

    /**
     * Invokes the concurrent write once, without a timing-dependent delay.
     * @param command The SQL about to execute.
     * @param eventData EF command metadata.
     * @param result Any existing interception result.
     * @param cancellationToken Cancels the concurrent write.
     * @return The unchanged command result after the writer commits.
     */
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (!ReplacementCommitted && command.CommandText.Contains("\"TripLegs\"", StringComparison.Ordinal))
        {
            await replace(cancellationToken);
            ReplacementCommitted = true;
        }
        return result;
    }
}
