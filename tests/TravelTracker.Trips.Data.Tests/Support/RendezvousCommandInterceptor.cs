using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelTracker.Trips.Data.Tests.Support;

/**
 * pauses a writer at its first trip UPDATE until the other writer reaches it too.
 * @param rendezvous the shared meeting point.
 */
internal sealed class RendezvousCommandInterceptor(WriteRendezvous rendezvous) : DbCommandInterceptor
{
    private int paused;

    /**
     * waits at the meeting point once, before the first trip UPDATE runs.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken cancels the wait.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("UPDATE \"Trips\"", StringComparison.Ordinal) && Interlocked.Exchange(ref paused, 1) == 0)
            await rendezvous.ArriveAsync();
        return result;
    }
}
