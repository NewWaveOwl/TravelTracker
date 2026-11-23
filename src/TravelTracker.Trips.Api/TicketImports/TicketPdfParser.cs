using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TravelTracker.Trips.Api.Contracts;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace TravelTracker.Trips.Api.TicketImports;

/** Extracts conservative proposals from explicitly labelled text PDFs, with no OCR or external processing. */
public sealed class TicketPdfParser : ITicketPdfParser
{
    public const int MaxPages = 10;
    public const int MaxTextCharacters = 100_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly string[] FieldNames = ["ticketType", "traveler", "operator", "bookingReference", "from", "to",
        "departureDate", "departureTime", "arrivalDate", "arrivalTime", "timeZone", "seat"];
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ticket type"] = "ticketType", ["Traveler"] = "traveler", ["Traveller"] = "traveler",
        ["Operator"] = "operator", ["Booking reference"] = "bookingReference", ["From"] = "from", ["To"] = "to",
        ["Departure date"] = "departureDate", ["Departure time"] = "departureTime", ["Arrival date"] = "arrivalDate",
        ["Arrival time"] = "arrivalTime", ["Time zone"] = "timeZone", ["Seat"] = "seat"
    };
    internal static readonly HashSet<string> UnsafeNames = new(StringComparer.Ordinal)
    {
        "Encrypt", "OpenAction", "AA", "JS", "JavaScript", "Launch", "EmbeddedFiles", "EmbeddedFile", "Filespec",
        "RichMedia", "XFA", "AcroForm", "ObjStm", "SubmitForm", "ImportData", "GoToR", "GoToE", "URI",
        "Form", "Type3", "Pattern", "FontFile", "FontFile2", "FontFile3", "ToUnicode", "CMap", "Type0",
        "CIDFontType0", "CIDFontType2", "Differences", "ExtGState", "Shading", "ActualText", "SMask", "OCProperties",
        "XRef", "XRefStm", "Prev"
    };

    /**
     * Checks document structure and page count before reading any page text.
     * @param content PDF bytes within the upload limit.
     * @param cancellationToken Cancels decompression and page reading.
     * @return Exactly the supported fields; an unread value is never inferred from another field.
     */
    public IReadOnlyList<TicketImportFieldResponse> Parse(byte[] content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CheckEnvelope(content, cancellationToken);
        var options = new ParsingOptions
        {
            UseLenientParsing = false,
            MaxStackDepth = 64,
            FilterProvider = new BoundedPdfFilterProvider(cancellationToken)
        };
        using var source = new BudgetedPdfStream(content, cancellationToken);
        using var document = PdfDocument.Open(source, options);
        if (document.IsEncrypted || document.NumberOfPages is < 1 or > MaxPages)
            throw new InvalidDataException("The PDF is encrypted or exceeds the page limit.");
        TicketPdfPreflight.Check(document, cancellationToken);

        var text = new StringBuilder();
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pageText = ContentOrderTextExtractor.GetText(document.GetPage(pageNumber));
            if (text.Length + pageText.Length > MaxTextCharacters)
                throw new InvalidDataException("The PDF exceeds the text limit.");
            text.AppendLine(pageText);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var values = FieldNames.ToDictionary(name => name, _ => new List<string>(), StringComparer.Ordinal);
        var foundLabel = false;
        foreach (var line in text.ToString().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || !Labels.TryGetValue(line[..separator].Trim(), out var name)) continue;
            foundLabel = true;
            var value = line[(separator + 1)..].Trim();
            if (value.Length > 0) values[name].Add(value);
        }
        if (!foundLabel) throw new InvalidDataException("No supported labelled ticket fields were found.");
        return FieldNames.Select(name => Propose(name, values[name])).ToArray();
    }

    /**
     * Rejects wrong signatures, truncated PDFs and features outside the supported static-text subset.
     * @param content Uploaded PDF bytes.
     * @param cancellationToken Stops feature scanning when the parse budget expires.
     */
    private static void CheckEnvelope(byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length < 16 || !(content.AsSpan(0, 7).SequenceEqual("%PDF-1."u8) && content[7] is >= (byte)'0' and <= (byte)'7' ||
            content.AsSpan(0, 8).SequenceEqual("%PDF-2.0"u8)))
            throw new InvalidDataException("The file signature is not a supported PDF.");
        var bytes = Encoding.Latin1.GetString(content);
        if (!bytes.TrimEnd('\0', '\t', '\r', '\n', ' ', '\f').EndsWith("%%EOF", StringComparison.Ordinal))
            throw new InvalidDataException("The PDF is incomplete.");
        var referenceCount = 0;
        const string separator = @"(?:[\x00\t\n\r\f ]|%[^\r\n]*(?:\r\n|\r|\n))+";
        foreach (Match reference in Regex.Matches(bytes, @"\b[0-9]+" + separator + "[0-9]+" + separator + @"R\b",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, RegexTimeout))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++referenceCount > 10_000) throw new InvalidDataException("Too many PDF object references.");
        }
        CheckCrossReferenceBudget(bytes, cancellationToken);
        foreach (Match match in Regex.Matches(bytes, @"/([A-Za-z0-9#]+)", RegexOptions.CultureInvariant, RegexTimeout))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Regex.Replace(match.Groups[1].Value, @"#([0-9a-fA-F]{2})",
                escaped => ((char)int.Parse(escaped.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString(),
                RegexOptions.CultureInvariant, RegexTimeout);
            if (UnsafeNames.Contains(name)) throw new InvalidDataException("This PDF contains unsupported document features.");
        }
    }

    /**
     * Bounds classic xref subsection declarations before the PDF library can build their collections.
     * @param bytes Latin-1 PDF envelope; binary xref streams are outside the supported subset.
     * @param cancellationToken Cancels preflight scanning.
     */
    private static void CheckCrossReferenceBudget(string bytes, CancellationToken cancellationToken)
    {
        var entries = 0;
        var tables = Regex.Matches(bytes, @"(?m)^[\t ]*xref[\t ]*(?:\r\n|\r|\n)", RegexOptions.CultureInvariant, RegexTimeout);
        var pointer = Regex.Match(bytes, @"startxref[\x00\t\n\r\f ]+([0-9]+)[\x00\t\n\r\f ]+%%EOF[\x00\t\n\r\f ]*$",
            RegexOptions.CultureInvariant, RegexTimeout);
        if (tables.Count != 1 || !pointer.Success ||
            !int.TryParse(pointer.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) ||
            offset != bytes.IndexOf("xref", tables[0].Index, StringComparison.Ordinal))
            throw new InvalidDataException("Only a single canonical PDF cross-reference table is supported.");
        foreach (Match table in tables)
        {
            var position = table.Index + table.Length;
            while (position < bytes.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var first = ReadWord(bytes, ref position);
                if (first.SequenceEqual("trailer")) break;
                if (!uint.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                    !int.TryParse(ReadWord(bytes, ref position), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0 ||
                    count > 2000 - entries)
                    throw new InvalidDataException("The PDF cross-reference table exceeds the supported limit.");
                entries += count;
                for (var index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!ulong.TryParse(ReadWord(bytes, ref position), NumberStyles.None, CultureInfo.InvariantCulture, out _) ||
                        !uint.TryParse(ReadWord(bytes, ref position), NumberStyles.None, CultureInfo.InvariantCulture, out _))
                        throw new InvalidDataException("Malformed PDF cross-reference table.");
                    var marker = ReadWord(bytes, ref position);
                    if (!marker.SequenceEqual("n") && !marker.SequenceEqual("f"))
                        throw new InvalidDataException("Malformed PDF cross-reference entry.");
                }
            }
        }
    }

    /**
     * Reads one whitespace-delimited envelope word without allocating a token list.
     * @param source PDF envelope.
     * @param position Updated to the first character after the word.
     * @return The word span, possibly empty at the end.
     */
    private static ReadOnlySpan<char> ReadWord(string source, ref int position)
    {
        while (position < source.Length && source[position] is '\0' or '\t' or '\n' or '\r' or '\f' or ' ') position++;
        var start = position;
        while (position < source.Length && source[position] is not '\0' and not '\t' and not '\n' and not '\r' and not '\f' and not ' ') position++;
        return source.AsSpan(start, position - start);
    }

    /**
     * Flags conflicts and unsupported formats rather than choosing an interpretation.
     * @param name Supported field name.
     * @param candidates Explicit values found under the same label.
     * @return An extracted proposal, missing value, or empty uncertain value.
     */
    private static TicketImportFieldResponse Propose(string name, List<string> candidates)
    {
        var distinct = candidates.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length == 0) return new(name, null, "missing");
        if (distinct.Length != 1 || distinct[0].Length > 200 || distinct[0].Any(char.IsControl))
            return new(name, null, "uncertain");
        var value = distinct[0];
        var valid = name switch
        {
            "departureDate" or "arrivalDate" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _),
            "departureTime" or "arrivalTime" => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _),
            "timeZone" => IsIanaZone(value),
            "ticketType" => value.ToLowerInvariant() is "flight" or "plane" or "train" or "bus" or "activity" or "accommodation",
            _ => true
        };
        return valid ? new(name, value, "extracted") : new(name, null, "uncertain");
    }

    /**
     * Accepts only an explicit IANA zone; abbreviations and offsets cannot establish a trip's time zone.
     * @param value Explicit source value.
     * @return Whether the host recognises the exact IANA identifier.
     */
    private static bool IsIanaZone(string value)
    {
        if (!value.Contains('/') && value != "Etc/UTC") return false;
        try { return TimeZoneInfo.FindSystemTimeZoneById(value).HasIanaId; }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }
}
