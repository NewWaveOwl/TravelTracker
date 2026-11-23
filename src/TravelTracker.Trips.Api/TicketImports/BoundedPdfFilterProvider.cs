using UglyToad.PdfPig.Filters;

namespace TravelTracker.Trips.Api.TicketImports;

/** Restricts PDF decoding to bounded Flate streams; other encodings require manual entry. */
internal sealed class BoundedPdfFilterProvider : BaseFilterProvider
{
    /**
     * Shares one decoded-byte budget across every Flate stream in the document.
     * @param cancellationToken Cancels decompression as well as page extraction.
     */
    internal BoundedPdfFilterProvider(CancellationToken cancellationToken) : this(new BoundedPdfFlateFilter(cancellationToken)) { }

    private BoundedPdfFilterProvider(BoundedPdfFlateFilter filter) : base(new Dictionary<string, IFilter>
    {
        ["FlateDecode"] = filter,
        ["Fl"] = filter
    }) { }
}
