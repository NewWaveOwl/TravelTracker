namespace TravelTracker.Trips.Api.TicketImports;

/**
 * Adds cancellation checks to parser reads during document opening and page-token scanning.
 * @param content The already bounded upload buffer.
 * @param cancellationToken Cooperative parser time budget and client cancellation.
 */
internal sealed class BudgetedPdfStream(byte[] content, CancellationToken cancellationToken) : MemoryStream(content, writable: false)
{
    /**
     * Checks the parse budget before reading the next block.
     * @param buffer Destination buffer.
     * @param offset Destination offset.
     * @param count Requested byte count.
     * @return Bytes read.
     */
    public override int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return base.Read(buffer, offset, count);
    }

    /**
     * Checks the parse budget before reading a span.
     * @param buffer Destination span.
     * @return Bytes read.
     */
    public override int Read(Span<byte> buffer)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return base.Read(buffer);
    }

    /**
     * Checks the parse budget for tokenizer byte reads.
     * @return The next byte, or -1 at the end.
     */
    public override int ReadByte()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return base.ReadByte();
    }

    /**
     * Checks the parse budget before seeking to another PDF object.
     * @param offset Seek offset.
     * @param loc Seek origin.
     * @return Resulting position.
     */
    public override long Seek(long offset, SeekOrigin loc)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return base.Seek(offset, loc);
    }
}
