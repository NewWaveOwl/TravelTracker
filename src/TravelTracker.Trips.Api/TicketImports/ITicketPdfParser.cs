using TravelTracker.Trips.Api.Contracts;

namespace TravelTracker.Trips.Api.TicketImports;

/** Reads only explicitly labelled supported fields on the team's own host. */
public interface ITicketPdfParser
{
    /**
     * Validates PDF structure and limits before extracting proposed fields.
     * @param content Bounded PDF bytes held in private request memory.
     * @param cancellationToken Cancels page reading and decompression.
     * @return Supported fields with missing and uncertain values flagged.
     * @throws InvalidDataException When the PDF is unsafe, malformed or unsupported.
     */
    IReadOnlyList<TicketImportFieldResponse> Parse(byte[] content, CancellationToken cancellationToken);
}
