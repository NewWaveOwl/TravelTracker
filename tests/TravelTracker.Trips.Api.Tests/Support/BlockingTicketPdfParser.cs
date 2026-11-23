using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Api.TicketImports;

namespace TravelTracker.Trips.Api.Tests.Support;

/** Holds two requests at the parser boundary to prove admission limits without timing-dependent sleeps. */
internal sealed class BlockingTicketPdfParser : ITicketPdfParser, IDisposable
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int count;

    internal Task BothEntered => entered.Task;

    /**
     * Signals admission, then waits until the test releases both bounded requests.
     * @param content Ignored synthetic PDF bytes.
     * @param cancellationToken Cancels the wait if the test fails to release it.
     * @return A synthetic proposed field.
     */
    public IReadOnlyList<TicketImportFieldResponse> Parse(byte[] content, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref count) == 2) entered.TrySetResult();
        released.Task.Wait(cancellationToken);
        return [new("operator", "Synthetic operator", "extracted")];
    }

    /** Releases the held parser calls, including on an assertion failure. */
    public void Dispose() => released.TrySetResult();
}
