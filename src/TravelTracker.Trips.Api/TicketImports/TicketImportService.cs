using System.Security.Cryptography;
using System.Text;
using TravelTracker.Api.References;
using TravelTracker.Api.Trips;
using TravelTracker.Trips.Api.Contracts;
using TravelTracker.Trips.Domain.Models;
using TravelTracker.Trips.Domain.TicketImport;
using TravelTracker.Trips.Data.Repositories;
using TravelTracker.Trips.Data.TicketImports;

namespace TravelTracker.Trips.Api.TicketImports;

/**
 * Coordinates access, bounded local parsing and temporary storage without writing the trip repository.
 * @param trips Performs only owner-scoped reads against existing trips.
 * @param usersStanding Confirms current account standing through Users.
 * @param parser Reads supported fields on this host.
 * @param documents Holds private temporary PDFs in bounded process memory.
 * @param capacity Limits simultaneous upload and parsing requests across the host.
 */
public sealed class TicketImportService(ITripRepository trips, UsersStandingClient usersStanding, ITicketPdfParser parser,
    ITicketImportStore documents, TicketImportCapacity capacity)
{
    /** largest pasted body in bytes; 4,000 UTF-16 units encode to at most 12,000 UTF-8 bytes. */
    public const int MaximumTextBytes = 12_000;

    /** shared refusal for pasted text over the byte bound. */
    private const string TextTooLarge = "The pasted ticket text must be no larger than 12,000 bytes (4,000 characters).";

    /**
     * Reads pasted plain ticket text into a review draft for an owned trip. Nothing is stored and the trip is not changed.
     * @param caller Validated issuer and subject, never request-supplied identity fields.
     * @param tripId Existing owned trip identifier.
     * @param source Raw text/plain request body, read no further than the byte bound.
     * @param contentType Request media type; only text/plain with UTF-8 or no charset is accepted.
     * @param contentLength Advertised length, checked again while reading.
     * @param cancellationToken Cancels reads and account or trip lookups.
     * @return A 200 draft in the PDF import's field shape, or a safe validation, permission or dependency outcome.
     */
    public async Task<TicketImportResult<TicketTextDraftResponse>> ParseTextAsync(CallerIdentity? caller, Guid tripId,
        Stream source, string? contentType, long? contentLength, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(caller, tripId, true, cancellationToken);
        if (access.StatusCode != 200) return new(access.StatusCode, Error: access.Error);
        if (!IsPlainUtf8Text(contentType))
            return new(415, Error: "Paste the ticket as plain text in UTF-8.");
        if (contentLength > MaximumTextBytes) return new(413, Error: TextTooLarge);

        var buffer = new byte[MaximumTextBytes + 1];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var read = await source.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (read == 0) break;
                length += read;
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
        {
            return new(413, Error: TextTooLarge);
        }
        if (length > MaximumTextBytes) return new(413, Error: TextTooLarge);

        string text;
        try { text = new UTF8Encoding(false, true).GetString(buffer, 0, length); }
        catch (DecoderFallbackException) { return new(400, Error: "The pasted ticket text must be valid UTF-8."); }

        try
        {
            // a byte order mark would otherwise stick to the first label and hide it.
            var draft = TicketTextParser.Parse(text.TrimStart('﻿'));
            return new(200, new(tripId, TicketTextFieldMapper.Map(draft)));
        }
        catch (TripValidationException exception)
        {
            return new(400, Error: exception.Errors[0].Message);
        }
    }

    /**
     * Accepts only plain text whose charset, when stated, is UTF-8; anything else is refused rather than guessed.
     * @param contentType Raw Content-Type header value.
     * @return True when the body may be decoded as UTF-8 text.
     */
    private static bool IsPlainUtf8Text(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var parts = contentType.Split(';', StringSplitOptions.TrimEntries);
        if (!string.Equals(parts[0], "text/plain", StringComparison.OrdinalIgnoreCase)) return false;
        return parts.Skip(1).All(part => !part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase)
            || string.Equals(part, "charset=utf-8", StringComparison.OrdinalIgnoreCase));
    }

    /**
     * Authorizes an owned active trip before reading or parsing the uploaded body.
     * @param caller Validated issuer and subject, never request-supplied identity fields.
     * @param tripId Existing trip identifier.
     * @param source Raw PDF request body; multipart uploads are deliberately unsupported to prevent disk spooling.
     * @param contentType Request media type.
     * @param contentLength Advertised length, checked again while reading even when absent or inaccurate.
     * @param fileName Optional encoded display name; it never determines a storage path.
     * @param cancellationToken Cancels reads, account checks, decoding and page extraction.
     * @return A temporary draft, or a safe validation, permission, quota or dependency outcome.
     */
    public async Task<TicketImportResult<TicketImportDraftResponse>> UploadAsync(CallerIdentity? caller, Guid tripId,
        Stream source, string? contentType, long? contentLength, string? fileName, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(caller, tripId, true, cancellationToken);
        if (access.StatusCode != 200) return new(access.StatusCode, Error: access.Error);
        if (!string.Equals(contentType?.Split(';')[0].Trim(), "application/pdf", StringComparison.OrdinalIgnoreCase))
            return new(415, Error: "Choose one PDF document using application/pdf.");
        if (contentLength > MemoryTicketImportStore.MaxFileBytes)
            return new(413, Error: "The PDF must be no larger than 10 MB (10,485,760 bytes).");
        if (!await capacity.Slots.WaitAsync(0, cancellationToken))
            return new(429, Error: "Ticket reading is busy. Try again shortly.");

        var buffer = new byte[MemoryTicketImportStore.MaxFileBytes + 1];
        byte[]? content = null;
        TicketImportDocument? document = null;
        try
        {
            using var uploadBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            uploadBudget.CancelAfter(TimeSpan.FromSeconds(30));
            var length = 0;
            try
            {
                while (length < buffer.Length)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(length), uploadBudget.Token);
                    if (read == 0) break;
                    length += read;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(408, Error: "The PDF upload took too long. Choose the document again.");
            }
            if (length > MemoryTicketImportStore.MaxFileBytes)
                return new(413, Error: "The PDF must be no larger than 10 MB (10,485,760 bytes).");
            if (length == 0) return new(400, Error: "Choose a non-empty PDF document.");
            content = buffer.AsSpan(0, length).ToArray();
            IReadOnlyList<TicketImportFieldResponse> fields;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(10));
                try { fields = parser.Parse(content, budget.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return new(422, Error: "This PDF took too long to read. Choose a simpler PDF or enter the details manually.");
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // Parser diagnostics can contain names, booking references or source bytes; never return or log them.
                    return new(422, Error: "This PDF could not be read. Use an unlocked, static text PDF of at most 10 pages, or enter details manually.");
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            document = documents.Add(caller!.Issuer, caller.Subject, tripId, DisplayName(fileName), content);
            if (document is null)
                return new(429, Error: "Temporary ticket storage is full. Cancel an earlier import or try again after it expires.");
            cancellationToken.ThrowIfCancellationRequested();
            return new(201, new(document.ImportId, tripId, document.FileName, document.ExpiresAt, fields));
        }
        catch (OperationCanceledException)
        {
            if (document is not null) documents.Remove(caller!.Issuer, caller.Subject, tripId, document.ImportId);
            throw;
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
        {
            return new(413, Error: "The PDF must be no larger than 10 MB (10,485,760 bytes).");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            if (content is not null) CryptographicOperations.ZeroMemory(content);
            if (document is not null) CryptographicOperations.ZeroMemory(document.Content);
            capacity.Slots.Release();
        }
    }

    /**
     * Reads a temporary PDF only after confirming live read access and the same owner/trip binding.
     * @param caller Validated identity.
     * @param tripId Existing owned trip.
     * @param importId Opaque temporary document id.
     * @param cancellationToken Cancels the account and trip lookups.
     * @return The private source PDF, or the same absent response for wrong owners and expired documents.
     */
    public async Task<TicketImportResult<TicketImportDocument>> GetDocumentAsync(CallerIdentity? caller, Guid tripId,
        Guid importId, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(caller, tripId, false, cancellationToken);
        if (access.StatusCode != 200) return new(access.StatusCode, Error: access.Error);
        var document = documents.Get(caller!.Issuer, caller.Subject, tripId, importId);
        return document is null ? new(404) : new(200, document);
    }

    /**
     * Cancels a temporary import without changing the existing trip.
     * @param caller Validated identity.
     * @param tripId Existing owned trip.
     * @param importId Temporary document to remove.
     * @param cancellationToken Cancels account and trip lookups.
     * @return 204 when removed, 404 when absent or inaccessible, or a permission/dependency failure.
     */
    public async Task<TicketImportResult<bool>> CancelAsync(CallerIdentity? caller, Guid tripId, Guid importId,
        CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(caller, tripId, true, cancellationToken);
        if (access.StatusCode != 200) return access;
        return documents.Remove(caller!.Issuer, caller.Subject, tripId, importId) ? new(204, true) : new(404);
    }

    /**
     * Confirms standing before looking for an owner-scoped trip; administrator status never grants another owner's access.
     * @param caller Validated token identity.
     * @param tripId Existing trip id.
     * @param writing Whether the operation requires active standing.
     * @param cancellationToken Cancels HTTP and database lookups.
     * @return 200 for allowed access or a safe refusal.
     */
    private async Task<TicketImportResult<bool>> CheckAccessAsync(CallerIdentity? caller, Guid tripId, bool writing,
        CancellationToken cancellationToken)
    {
        if (caller is null) return new(401, Error: "A validated issuer and subject are required.");
        var standing = await usersStanding.GetStandingAsync(caller, cancellationToken);
        if (standing.Status == ReferenceStatus.NotFound) return new(403, Error: "Your account cannot access ticket imports.");
        if (standing.Status != ReferenceStatus.Found) return new(503, Error: "Account access could not be verified. Try again later.");
        if (standing.Value!.Status == "deactivated" || writing && standing.Value.Status != "active")
            return new(403, Error: "Your account cannot change ticket imports.");
        return await trips.GetOwnedRevisionAsync(tripId, caller.Issuer, caller.Subject, cancellationToken) is null
            ? new(404)
            : new(200, true);
    }

    /**
     * Produces a safe display name without paths, control characters or unbounded metadata.
     * @param encodedName Optional URI-encoded client filename.
     * @return A PDF basename used only as display metadata and a download label.
     */
    private static string DisplayName(string? encodedName)
    {
        if (string.IsNullOrWhiteSpace(encodedName)) return "ticket.pdf";
        string decoded;
        try { decoded = Uri.UnescapeDataString(encodedName.Length <= 1024 ? encodedName : encodedName[..1024]); }
        catch (UriFormatException) { return "ticket.pdf"; }
        var name = Path.GetFileName(decoded.Replace('\\', '/'));
        name = new string(name.Where(character => !char.IsControl(character) && character is not '"' and not '<' and not '>').Take(120).ToArray()).Trim();
        if (name.Length == 0) return "ticket.pdf";
        return name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? name : name + ".pdf";
    }
}
