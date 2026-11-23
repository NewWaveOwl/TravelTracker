using System.IO.Compression;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace TravelTracker.Trips.Api.TicketImports;

/**
 * Rejects compressed streams before they can expand beyond the document's decoded-byte budget.
 * @param cancellationToken Cancels decompression between bounded reads.
 */
internal sealed class BoundedPdfFlateFilter(CancellationToken cancellationToken) : IFilter
{
    internal const int MaxStreamBytes = 4 * 1024 * 1024;
    internal const int MaxDocumentBytes = 16 * 1024 * 1024;
    private int decodedBytes;

    public bool IsSupported => true;

    /**
     * Decodes ordinary Flate streams with no pixel predictor and a fixed memory ceiling.
     * @param input Compressed bytes supplied by PdfPig.
     * @param streamDictionary Stream's PDF dictionary.
     * @param filterProvider Provider supplied by PdfPig.
     * @param filterIndex Index of this filter in the decoding chain.
     * @return The bounded decoded stream.
     */
    public Memory<byte> Decode(Memory<byte> input, DictionaryToken streamDictionary, IFilterProvider filterProvider, int filterIndex)
    {
        var parameters = DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
        if (parameters is not null && parameters.TryGet(NameToken.Predictor, out var predictor) &&
            predictor is not NumericToken { Int: 1 })
            throw new InvalidDataException("Unsupported PDF compression parameters.");

        using var source = new MemoryStream(input.ToArray(), writable: false);
        using var inflater = new ZLibStream(source, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = inflater.Read(buffer)) != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (output.Length + count > MaxStreamBytes || decodedBytes + count > MaxDocumentBytes)
                throw new InvalidDataException("PDF decompression limit exceeded.");
            output.Write(buffer, 0, count);
            decodedBytes += count;
        }
        return output.ToArray();
    }
}
