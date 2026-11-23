using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace TravelTracker.Trips.Api.TicketImports;

/** Bounds content expansion before PdfPig can allocate page operation and Letter collections. */
internal static class TicketPdfPreflight
{
    private const int MaxPageBytes = 100_000;
    private const int MaxPageStringBytes = 10_000;
    private const int MaxPageTokens = 20_000;
    private static readonly HashSet<string> StandardFonts = new(StringComparer.Ordinal)
    {
        "Courier", "Courier-Bold", "Courier-Oblique", "Courier-BoldOblique", "Helvetica", "Helvetica-Bold",
        "Helvetica-Oblique", "Helvetica-BoldOblique", "Times-Roman", "Times-Bold", "Times-Italic", "Times-BoldItalic",
        "Symbol", "ZapfDingbats"
    };

    /**
     * Checks font and page content budgets before calling the default page renderer even once.
     * @param document Strictly opened document within the page limit.
     * @param cancellationToken Stops preflight reads and decoding.
     */
    internal static void Check(PdfDocument document, CancellationToken cancellationToken)
    {
        var references = document.Structure.CrossReferenceTable.ObjectOffsets;
        if (references.Count > 2000) throw new InvalidDataException("Too many PDF objects.");
        var objectTokens = 0;
        foreach (var reference in references.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckFonts(document.Structure.GetObject(reference).Data, ref objectTokens, 0);
        }
        document.AddPageFactory<DictionaryToken>(new TicketPageDictionaryFactory());
        for (var number = 1; number <= document.NumberOfPages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = document.GetPage<DictionaryToken>(number);
            if (!page.TryGet(NameToken.Contents, out var contents)) continue;
            var resolved = Resolve(document, contents);
            IReadOnlyList<IToken> items = resolved is ArrayToken array ? array.Data : [resolved];
            if (items.Count > 16) throw new InvalidDataException("Too many PDF page streams.");
            var pageBytes = 0;
            var pageStringBytes = 0;
            var pageTokens = 0;
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Resolve(document, item) is not StreamToken stream)
                    throw new InvalidDataException("Invalid PDF page contents.");
                var data = stream.Data;
                var filters = document.Structure.FilterProvider.GetFilters(stream.StreamDictionary, document.Structure.TokenScanner);
                for (var index = 0; index < filters.Count; index++)
                    data = filters[index].Decode(data, stream.StreamDictionary, document.Structure.FilterProvider, index);
                pageBytes += data.Length;
                if (pageBytes > MaxPageBytes) throw new InvalidDataException("PDF page content exceeds the preflight limit.");
                CheckContent(data.Span, ref pageStringBytes, ref pageTokens, cancellationToken);
            }
        }
    }

    /**
     * Rejects nonstandard or embedded fonts, whose glyph programs and maps can amplify tiny text operands.
     * @param token Token to inspect without following references recursively.
     * @param count Shared object-token budget.
     * @param depth Current dictionary/array nesting depth.
     */
    private static void CheckFonts(IToken token, ref int count, int depth)
    {
        if (++count > 20_000 || depth > 32) throw new InvalidDataException("PDF object complexity exceeds the limit.");
        if (token is StreamToken stream) token = stream.StreamDictionary;
        if (token is DictionaryToken dictionary)
        {
            if (dictionary.Data.Keys.Any(TicketPdfParser.UnsafeNames.Contains) ||
                dictionary.Data.Values.OfType<NameToken>().Any(name => TicketPdfParser.UnsafeNames.Contains(name.Data)))
                throw new InvalidDataException("Unsupported PDF resource dictionary.");
            if (dictionary.TryGet(NameToken.BaseFont, out var font) ||
                dictionary.TryGet(NameToken.Type, out var type) && type is NameToken { Data: "Font" })
            {
                if (font is not NameToken baseFont || !StandardFonts.Contains(baseFont.Data))
                    throw new InvalidDataException("Only standard PDF text fonts are supported.");
            }
            foreach (var child in dictionary.Data.Values) CheckFonts(child, ref count, depth + 1);
        }
        else if (token is ArrayToken array)
            foreach (var child in array.Data) CheckFonts(child, ref count, depth + 1);
    }

    /**
     * Resolves one indirect token; nested references are outside the supported static subset.
     * @param document Open document.
     * @param token Direct or indirect token.
     * @return The resolved token.
     */
    private static IToken Resolve(PdfDocument document, IToken token) => token is IndirectReferenceToken reference
        ? document.Structure.GetObject(reference.Data).Data
        : token;

    /**
     * Counts strings and lexical operands before rendering, refusing forms, inline images and oversized glyph candidates.
     * @param content One decoded content stream within the page byte budget.
     * @param stringBytes Shared per-page glyph candidate bound, counting reused streams each time.
     * @param tokens Shared per-page operation/operand bound.
     * @param cancellationToken Stops the bounded lexical scan.
     */
    private static void CheckContent(ReadOnlySpan<byte> content, ref int stringBytes, ref int tokens, CancellationToken cancellationToken)
    {
        var index = 0;
        while (index < content.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = content[index++];
            if (IsWhite(current)) continue;
            if (current == '%')
            {
                while (index < content.Length && content[index] is not (byte)'\r' and not (byte)'\n') index++;
                continue;
            }
            if (++tokens > MaxPageTokens) throw new InvalidDataException("Too many PDF page operations.");
            if (current == '(')
            {
                var depth = 1;
                while (index < content.Length && depth > 0)
                {
                    var character = content[index++];
                    if (character == '\\' && index < content.Length) index++;
                    else if (character == '(') { if (++depth > 32) throw new InvalidDataException("Nested PDF string limit exceeded."); }
                    else if (character == ')') depth--;
                    if (++stringBytes > MaxPageStringBytes) throw new InvalidDataException("Too many PDF text glyph candidates.");
                }
                if (depth != 0) throw new InvalidDataException("Unterminated PDF text.");
                continue;
            }
            if (current == '<' && (index == content.Length || content[index] != '<'))
            {
                var digits = 0;
                while (index < content.Length && content[index] != '>')
                {
                    if (!IsWhite(content[index++])) digits++;
                    if ((digits + 1) / 2 + stringBytes > MaxPageStringBytes)
                        throw new InvalidDataException("Too many PDF text glyph candidates.");
                }
                if (index == content.Length) throw new InvalidDataException("Unterminated PDF hex string.");
                index++;
                stringBytes += (digits + 1) / 2;
                continue;
            }
            if (current is (byte)'[' or (byte)']' or (byte)'<' or (byte)'>' or (byte)'{' or (byte)'}') continue;
            var start = index - 1;
            while (index < content.Length && !IsWhite(content[index]) && content[index] is not (byte)'(' and not (byte)'<' and not (byte)'[' and not (byte)']' and not (byte)'%' and not (byte)'/' and not (byte)')' and not (byte)'>' and not (byte)'{' and not (byte)'}') index++;
            var value = Encoding.ASCII.GetString(content[start..index]);
            if (value is "Do" or "BI") throw new InvalidDataException("PDF forms and inline images require manual entry.");
        }
    }

    private static bool IsWhite(byte value) => value is 0 or 9 or 10 or 12 or 13 or 32;
}
