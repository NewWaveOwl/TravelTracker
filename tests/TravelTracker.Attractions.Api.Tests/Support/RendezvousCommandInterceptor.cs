using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelTracker.Attractions.Api.Tests;

/**
 * pauses each writer at the statement that takes its row lock until every writer has reached it.
 * @param rendezvous the shared meeting point.
 * @param commandMarker text that identifies the locking statement.
 */
internal sealed class RendezvousCommandInterceptor(WriteRendezvous rendezvous, string commandMarker) : DbCommandInterceptor
{
    /**
     * waits at the meeting point before the locking statement runs.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken unused; the rendezvous has its own timeout.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains(commandMarker, StringComparison.Ordinal)) await rendezvous.ArriveAsync();
        return result;
    }
}
