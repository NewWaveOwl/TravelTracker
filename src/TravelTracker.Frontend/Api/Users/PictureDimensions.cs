using System.Buffers.Binary;

namespace TravelTracker.Frontend.Api.Users;

/** Reads known PNG/JPEG header dimensions for early size feedback; the API still validates the decoded image. */
internal static class PictureDimensions
{
    /**
     * Rejects a recognizable image header whose dimensions exceed the upload contract, without decoding pixels.
     * @param picture Already byte-bounded upload content.
     * @param contentType The supported PNG or JPEG media type.
     * @param maximumSide Maximum permitted width and height.
     * @return True for known oversized dimensions; unreadable headers remain subject to API validation.
     */
    internal static bool ExceedsLimit(ReadOnlySpan<byte> picture, string contentType, int maximumSide)
    {
        if (string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase))
        {
            if (picture.Length < 33 || !picture[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
                BinaryPrimitives.ReadUInt32BigEndian(picture[8..12]) != 13 || !picture[12..16].SequenceEqual("IHDR"u8)) return false;
            return BinaryPrimitives.ReadUInt32BigEndian(picture[16..20]) > maximumSide ||
                BinaryPrimitives.ReadUInt32BigEndian(picture[20..24]) > maximumSide;
        }
        if (!string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
            picture.Length < 4 || picture[0] != 0xff || picture[1] != 0xd8) return false;

        var offset = 2;
        while (offset < picture.Length)
        {
            if (picture[offset++] != 0xff) return false;
            while (offset < picture.Length && picture[offset] == 0xff) offset++;
            if (offset >= picture.Length) return false;
            var marker = picture[offset++];
            if (marker is 0x00 or 0xd9 or 0xda) return false;
            if (marker is 0x01 or 0xd8 or >= 0xd0 and <= 0xd7) continue;
            if (picture.Length - offset < 2) return false;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(picture[offset..]);
            if (segmentLength < 2 || segmentLength > picture.Length - offset) return false;
            // Start-of-frame markers carry dimensions; Huffman/DAC tables and APP metadata do not.
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc))
            {
                if (segmentLength < 8 || picture[offset + 7] == 0 || segmentLength != 8 + 3 * picture[offset + 7]) return false;
                return BinaryPrimitives.ReadUInt16BigEndian(picture[(offset + 3)..]) > maximumSide ||
                    BinaryPrimitives.ReadUInt16BigEndian(picture[(offset + 5)..]) > maximumSide;
            }
            offset += segmentLength;
        }
        return false;
    }
}
