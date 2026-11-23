namespace TravelTracker.Trips.Api.TicketImports;

/** Bounds simultaneous upload buffers and local parser work across the whole host. */
public sealed class TicketImportCapacity : IDisposable
{
    internal SemaphoreSlim Slots { get; } = new(2, 2);

    /** Releases the host-owned semaphore after request services have stopped. */
    public void Dispose() => Slots.Dispose();
}
