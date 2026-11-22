using System.Buffers.Binary;
using Microsoft.Net.Http.Headers;
using SkiaSharp;

namespace TravelTracker.Users.Api.Services;

/** Decodes bounded PNG or JPEG input and normalizes pixels to a metadata-free PNG. */
internal static class ProfilePictureUpload
{
    internal const int MaxBytes = 1024 * 1024;
    private const int MaxSide = 512;
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static ReadOnlySpan<byte> JpegSignature => [255, 216, 255];

    /**
     * Validates an upload before persistence is attempted.
     * @param request The request carrying a raw image body.
     * @param cancellationToken Cancels body I/O or prevents starting further image processing.
     * @return A status for invalid input, or normalized PNG bytes for a valid upload.
     */
    internal static async Task<(int Status, byte[]? Picture)> DecodeAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) ||
            !string.IsNullOrEmpty(request.Headers.ContentEncoding))
            return (StatusCodes.Status415UnsupportedMediaType, null);
        var jpeg = string.Equals(type.MediaType.Value, "image/jpeg", StringComparison.OrdinalIgnoreCase);
        if (!jpeg && !string.Equals(type.MediaType.Value, "image/png", StringComparison.OrdinalIgnoreCase))
            return (StatusCodes.Status415UnsupportedMediaType, null);
        if (request.ContentLength > MaxBytes)
            return (StatusCodes.Status413PayloadTooLarge, null);

        var buffer = new byte[MaxBytes + 1];
        var count = await request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, cancellationToken);
        if (count > MaxBytes)
            return (StatusCodes.Status413PayloadTooLarge, null);
        if (count == 0)
            return (StatusCodes.Status400BadRequest, null);

        var bytes = buffer.AsSpan(0, count);
        if (!bytes.StartsWith(jpeg ? JpegSignature : PngSignature))
            return (StatusCodes.Status415UnsupportedMediaType, null);
        var containerStatus = jpeg
            ? (bytes[^2] == 0xff && bytes[^1] == 0xd9 ? 0 : StatusCodes.Status400BadRequest)
            : ValidatePngContainer(bytes);
        if (containerStatus != 0)
            return (containerStatus, null);

        cancellationToken.ThrowIfCancellationRequested();
        using var input = new MemoryStream(buffer, 0, count, writable: false);
        using var decoder = SKCodec.Create(input);
        if (decoder is null)
            return (StatusCodes.Status400BadRequest, null);
        if (decoder.EncodedFormat != (jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png))
            return (StatusCodes.Status415UnsupportedMediaType, null);
        var size = decoder.Info;
        if (size.Width is < 1 or > MaxSide || size.Height is < 1 or > MaxSide)
            return (StatusCodes.Status413PayloadTooLarge, null);
        using var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (decoder.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            return (StatusCodes.Status400BadRequest, null);

        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Profile picture encoding failed.");
        return (StatusCodes.Status204NoContent, encoded.ToArray());
    }

    /**
     * Checks the bounded PNG container for a complete end marker and rejects animation.
     * @param bytes The input after matching the PNG signature.
     * @return Zero for a complete static container, or the rejection status.
     */
    private static int ValidatePngContainer(ReadOnlySpan<byte> bytes)
    {
        var remaining = bytes[PngSignature.Length..];
        while (remaining.Length >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(remaining);
            if (length > remaining.Length - 12) return StatusCodes.Status400BadRequest;
            var type = remaining.Slice(4, 4);
            remaining = remaining[(12 + (int)length)..];
            if (type.SequenceEqual("acTL"u8)) return StatusCodes.Status415UnsupportedMediaType;
            if (type.SequenceEqual("IEND"u8))
                return length == 0 && remaining.IsEmpty ? 0 : StatusCodes.Status400BadRequest;
        }
        return StatusCodes.Status400BadRequest;
    }
}
