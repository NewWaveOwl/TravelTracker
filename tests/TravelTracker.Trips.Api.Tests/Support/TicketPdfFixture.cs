using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TravelTracker.Trips.Api.Tests.Support;

/** Builds synthetic static ticket PDFs without using any traveller document or external fixtures. */
internal static class TicketPdfFixture
{
    internal static readonly string[] CompleteTicket = ["Ticket type: Train", "Traveler: Synthetic Traveler", "Operator: Test Rail",
        "Booking reference: SYNTHETIC42", "From: Test Station A", "To: Test Station B", "Departure date: 2026-11-23",
        "Departure time: 08:35", "Arrival date: 2026-11-23", "Arrival time: 09:00", "Time zone: America/Toronto", "Seat: 12A"];

    /**
     * Writes a complete PDF including a correct xref table, with optional compression or Standard RC4 encryption.
     * @param lines Labelled ticket lines, or empty for a scanned/unsupported document.
     * @param pages Number of pages; all pages reference the same bounded content.
     * @param compressed Whether the content uses Flate compression.
     * @param encrypted Whether to encrypt with an empty user password, proving these are refused too.
     * @param extraName Optional feature name in the catalog used by rejection cases.
     * @param inflatedPadding Additional whitespace in the page content before compression.
     * @param envelopePadding Inert comment bytes before the xref table for upload size boundary tests.
     * @return A self-contained synthetic PDF.
     */
    internal static byte[] Build(string[]? lines = null, int pages = 1, bool compressed = false, bool encrypted = false,
        string? extraName = null, int inflatedPadding = 0, int envelopePadding = 0)
    {
        var commands = new StringBuilder("BT /F1 12 Tf 40 780 Td ");
        foreach (var line in lines ?? CompleteTicket)
            commands.Append('(').Append(line.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)")).Append(") Tj 0 -20 Td ");
        commands.Append("ET").Append(' ', inflatedPadding);
        var content = Encoding.ASCII.GetBytes(commands.ToString());
        if (compressed)
        {
            using var encoded = new MemoryStream();
            using (var compressor = new ZLibStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true)) compressor.Write(content);
            content = encoded.ToArray();
        }
        var objects = new List<byte[]>();
        var fontId = 4 + pages;
        var encryptionId = fontId + 1;
        var catalog = $"<< /Type /Catalog /Pages 2 0 R {(extraName is null ? "" : "/" + extraName + " null")} >>";
        objects.Add(Encoding.ASCII.GetBytes(catalog));
        objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Pages /Count {pages} /Kids [ {string.Join(' ', Enumerable.Range(4, pages).Select(id => $"{id} 0 R"))} ] >>"));

        var idBytes = MD5.HashData("synthetic-ticket-fixture"u8);
        byte[]? owner = null;
        byte[]? user = null;
        if (encrypted)
        {
            byte[] padding = [0x28, 0xbf, 0x4e, 0x5e, 0x4e, 0x75, 0x8a, 0x41, 0x64, 0x00, 0x4e, 0x56, 0xff, 0xfa, 0x01, 0x08,
                0x2e, 0x2e, 0x00, 0xb6, 0xd0, 0x68, 0x3e, 0x80, 0x2f, 0x0c, 0xa9, 0xfe, 0x64, 0x53, 0x69, 0x7a];
            var ownerPassword = Encoding.ASCII.GetBytes("synthetic-owner");
            var ownerPad = ownerPassword.Concat(padding).Take(32).ToArray();
            owner = Rc4(MD5.HashData(ownerPad)[..5], padding);
            var permissions = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(permissions, -4);
            var key = MD5.HashData(padding.Concat(owner).Concat(permissions).Concat(idBytes).ToArray())[..5];
            user = Rc4(key, padding);
            var objectKey = MD5.HashData(key.Concat(new byte[] { 3, 0, 0, 0, 0 }).ToArray())[..10];
            content = Rc4(objectKey, content);
        }
        var header = Encoding.ASCII.GetBytes($"<< /Length {content.Length}{(compressed ? " /Filter /FlateDecode" : "")} >>\nstream\n");
        objects.Add(header.Concat(content).Concat("\nendstream"u8.ToArray()).ToArray());
        for (var index = 0; index < pages; index++)
            objects.Add(Encoding.ASCII.GetBytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 842] /Resources << /Font << /F1 {fontId} 0 R >> >> /Contents 3 0 R >>"));
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        if (encrypted) objects.Add(Encoding.ASCII.GetBytes($"<< /Filter /Standard /V 1 /R 2 /Length 40 /O <{Convert.ToHexString(owner!)}> /U <{Convert.ToHexString(user!)}> /P -4 >>"));

        using var result = new MemoryStream();
        Write(result, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(result.Position);
            Write(result, $"{index + 1} 0 obj\n");
            result.Write(objects[index]);
            Write(result, "\nendobj\n");
        }
        if (envelopePadding >= 2) Write(result, "%" + new string(' ', envelopePadding - 2) + "\n");
        var xref = result.Position;
        Write(result, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(result, $"{offset:D10} 00000 n \n");
        Write(result, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /ID [<{Convert.ToHexString(idBytes)}> <{Convert.ToHexString(idBytes)}>] {(encrypted ? $"/Encrypt {encryptionId} 0 R" : "")} >>\nstartxref\n{xref}\n%%EOF\n");
        return result.ToArray();
    }

    /**
     * Implements the historical test fixture's Standard encryption algorithm; production never decrypts tickets.
     * @param key Synthetic fixture key.
     * @param input Synthetic content or password padding.
     * @return RC4 fixture bytes.
     */
    private static byte[] Rc4(byte[] key, byte[] input)
    {
        var state = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var j = 0;
        for (var i = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 255;
            (state[i], state[j]) = (state[j], state[i]);
        }
        var output = new byte[input.Length];
        var first = 0;
        j = 0;
        for (var index = 0; index < input.Length; index++)
        {
            first = (first + 1) & 255;
            j = (j + state[first]) & 255;
            (state[first], state[j]) = (state[j], state[first]);
            output[index] = (byte)(input[index] ^ state[(state[first] + state[j]) & 255]);
        }
        return output;
    }

    /**
     * Writes ASCII PDF syntax without a byte-order mark.
     * @param stream Fixture destination.
     * @param text Syntax fragment.
     */
    private static void Write(Stream stream, string text) => stream.Write(Encoding.ASCII.GetBytes(text));
}
