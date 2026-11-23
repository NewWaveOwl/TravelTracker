using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * pauses each settings write at its database statement until the racing writer reaches its own statement.
 * only the race armed in Current is affected; other requests pass straight through.
 */
internal sealed class RendezvousCommandInterceptor : DbCommandInterceptor
{
    /** the meeting point for the race in progress, or null when no race is armed. */
    public WriteRendezvous? Current { get; set; }

    /**
     * waits at the meeting point before a settings INSERT or UPDATE runs.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken unused; the rendezvous has its own timeout.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await PauseSettingsWriteAsync(command);
        return result;
    }

    /**
     * waits at the meeting point before a settings INSERT or UPDATE that returns rows runs.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken unused; the rendezvous has its own timeout.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        await PauseSettingsWriteAsync(command);
        return result;
    }

    /**
     * waits at the meeting point before a settings INSERT or UPDATE that returns a scalar runs.
     * @param command the command about to run.
     * @param eventData command details.
     * @param result the interception result.
     * @param cancellationToken unused; the rendezvous has its own timeout.
     * @return the unchanged interception result.
     */
    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
        InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        await PauseSettingsWriteAsync(command);
        return result;
    }

    /**
     * arrives at the armed rendezvous when the command writes the settings table.
     * @param command the command about to run.
     * @return completion once the race has released this writer.
     */
    private async Task PauseSettingsWriteAsync(DbCommand command)
    {
        var rendezvous = Current;
        if (rendezvous is null) return;
        if (command.CommandText.Contains("UPDATE \"UserSettings\"", StringComparison.Ordinal) ||
            command.CommandText.Contains("INSERT INTO \"UserSettings\"", StringComparison.Ordinal))
            await rendezvous.ArriveAsync();
    }
}
