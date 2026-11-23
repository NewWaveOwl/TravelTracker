namespace TravelTracker.Trips.Data.TicketImports;

/**
 * Returns temporary PDF metadata and a defensive content copy that the caller owns.
 *
 * @param ImportId The random temporary document identifier.
 * @param TripId The public trip identifier.
 * @param FileName The document's display name, never a filesystem path.
 * @param Content A defensive copy of the PDF bytes, which the caller may clear after use.
 * @param ExpiresAt The absolute expiry; reading never extends it.
 */
public sealed record TicketImportDocument(Guid ImportId, Guid TripId, string FileName, byte[] Content, DateTimeOffset ExpiresAt);
