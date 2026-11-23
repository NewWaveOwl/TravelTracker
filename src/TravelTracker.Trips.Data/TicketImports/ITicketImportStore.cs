namespace TravelTracker.Trips.Data.TicketImports;

/** Stores short-lived PDF documents behind an exact owner identity and trip boundary. */
public interface ITicketImportStore
{
    /**
     * Copies one document into private temporary storage.
     *
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param tripId The public identifier of the owner's trip.
     * @param fileName The display name supplied by the API, never a filesystem path.
     * @param content The validated PDF bytes to copy.
     * @return A document with independently owned bytes, or null for invalid input or exhausted capacity.
     */
    TicketImportDocument? Add(string issuer, string subject, Guid tripId, string fileName, ReadOnlyMemory<byte> content);

    /**
     * Copies an unexpired document belonging to the exact owner and trip.
     *
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param tripId The public identifier of the owner's trip.
     * @param importId The random temporary document identifier.
     * @return A document with independently owned bytes, or null when absent, expired or inaccessible.
     */
    TicketImportDocument? Get(string issuer, string subject, Guid tripId, Guid importId);

    /**
     * Clears and removes an unexpired document belonging to the exact owner and trip.
     *
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param tripId The public identifier of the owner's trip.
     * @param importId The random temporary document identifier.
     * @return True when removed; false when absent, expired or inaccessible.
     */
    bool Remove(string issuer, string subject, Guid tripId, Guid importId);
}
