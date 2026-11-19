namespace TravelTracker.Settings.Api.Tests.Support;

/**
 * holds each participant until all of them have arrived, so concurrent writers provably overlap.
 * @param participants how many writers must arrive before any continues.
 */
internal sealed class WriteRendezvous(int participants)
{
    private readonly TaskCompletionSource allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrived;

    /** whether every participant arrived. */
    public bool Completed => allArrived.Task.IsCompletedSuccessfully;

    /**
     * signals arrival and waits for the others.
     * @return completion once all participants arrived.
     * @throws TimeoutException when the others never arrive within ten seconds.
     */
    public async Task ArriveAsync()
    {
        if (Interlocked.Increment(ref arrived) == participants) allArrived.TrySetResult();
        await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
