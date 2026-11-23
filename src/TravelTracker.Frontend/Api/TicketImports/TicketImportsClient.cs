namespace TravelTracker.Frontend.Api.TicketImports;

/**
 * Exchanges the public PDF import contract through the authenticated Trips boundary.
 * @param client Enforces destinations, session expiry and revocation.
 */
public sealed class TicketImportsClient(ProtectedClient client)
{
    public const long MaximumBytes = 10 * 1024 * 1024;

    /**
     * Uploads one PDF for an editable draft without changing the trip.
     * @param tripId Public owned trip identifier.
     * @param source Bounded source stream from the picker.
     * @param fileName Display filename, never a storage path.
     * @param token Cancels the upload.
     * @return The server's temporary extraction draft.
     */
    public async Task<TicketImportDto> UploadAsync(Guid tripId, Stream source, string fileName, CancellationToken token)
    {
        try
        {
            var draft = await client.UploadPdfAsync<TicketImportDto>(Path(tripId), source, fileName, token)
                ?? throw new ApiError(FailureKind.Unexpected, "The service returned no ticket draft. Your trip is unchanged.");
            if (draft.TripId != tripId || draft.ImportId == Guid.Empty || draft.Fields is null)
                throw new ApiError(FailureKind.Unexpected, "The service returned an invalid ticket draft. Your trip is unchanged.");
            return draft;
        }
        catch (ApiError error) { throw ForTicket(error); }
    }

    /**
     * Downloads the original temporary PDF after the service rechecks ownership.
     * @param tripId Public owned trip identifier.
     * @param importId Opaque temporary import identifier.
     * @param token Cancels the download.
     * @return The private PDF content.
     */
    public async Task<byte[]> GetDocumentAsync(Guid tripId, Guid importId, CancellationToken token)
    {
        try { return await client.GetPdfAsync($"{Path(tripId)}/{importId:D}/document", token); }
        catch (ApiError error) { throw ForTicket(error); }
    }

    /**
     * Releases a temporary document; a missing or already expired import is harmless.
     * @param tripId Public owned trip identifier.
     * @param importId Opaque temporary import identifier.
     * @param token Cancels cleanup.
     * @return Completion after removal or a non-disclosing not-found response.
     */
    public async Task CancelAsync(Guid tripId, Guid importId, CancellationToken token)
    {
        try { await client.DeleteTicketAsync($"{Path(tripId)}/{importId:D}", token); }
        catch (ApiError error) when (error.Status == 404) { }
        catch (ApiError error) { throw ForTicket(error); }
    }

    static string Path(Guid tripId) => $"/api/v1/trips/{tripId:D}/ticket-imports";

    static ApiError ForTicket(ApiError error) => new(error.Kind, error.Status switch
    {
        400 => "This PDF is invalid. Choose a complete, readable PDF or enter the details manually. Your trip is unchanged.",
        401 => "Your session has ended. Sign in again to import a ticket.",
        403 => "Your account or permissions do not allow importing tickets. Your trip is unchanged.",
        404 => "This trip or temporary document is unavailable. It may have expired; reopen the trip or choose the PDF again.",
        408 => "The ticket upload timed out. Your trip is unchanged. Choose the PDF again or enter the details manually.",
        413 => "The PDF is too large. Choose a PDF up to 10 MB and 10 pages.",
        415 => "Choose a PDF document. Other file types are not supported.",
        422 => "We could not read supported ticket details from this PDF. Use an unlocked, text-based PDF of up to 10 pages, or enter the details manually.",
        429 => "Ticket import is busy or temporary storage is full. Try again shortly, or cancel an earlier import.",
        >= 500 => "Ticket import is temporarily unavailable. Your trip is unchanged. Try again or enter the details manually.",
        _ => error.Message
    }, error.Status, error.Errors);
}
