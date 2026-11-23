using System.Net;

namespace TravelTracker.Trips.Api.Tests.Support;

/**
 * Streams a synthetic body without declaring Content-Length so the server must enforce its read bound.
 * @param bytes Synthetic upload content.
 */
internal sealed class UnknownLengthPdfContent(byte[] bytes) : HttpContent
{
    /**
     * Writes the fixture to the HTTP transport.
     * @param stream HTTP destination.
     * @param context Unused transport context.
     * @return Completion after sending the bytes.
     */
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

    /**
     * Omits the length header.
     * @param length Unused length.
     * @return False so HTTP uses streaming framing.
     */
    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
