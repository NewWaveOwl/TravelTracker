using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Outline.Destinations;
using UglyToad.PdfPig.Tokens;

namespace TravelTracker.Trips.Api.TicketImports;

/** Returns page dictionaries for preflight without allocating graphics operations or rendered letters. */
internal sealed class TicketPageDictionaryFactory : IPageFactory<DictionaryToken>
{
    /**
     * Returns the raw page dictionary before the default page renderer is invoked.
     * @param number One-based page number.
     * @param dictionary Raw page dictionary.
     * @param pageTreeMembers Inherited page attributes; not rendered during preflight.
     * @param namedDestinations Document destinations; not followed by ticket extraction.
     * @return The original page dictionary for bounded content validation.
     */
    public DictionaryToken Create(int number, DictionaryToken dictionary, PageTreeMembers pageTreeMembers,
        NamedDestinations namedDestinations) => dictionary;
}
