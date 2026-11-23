using System.Security.Cryptography;

namespace TravelTracker.Trips.Data.TicketImports;

/**
 * Keeps bounded, temporary PDF copies only in this process, clearing retained bytes on expiry, cancellation and shutdown.
 * No document survives a restart or becomes a public static file.
 */
public sealed class MemoryTicketImportStore : ITicketImportStore, IDisposable
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public const int MaxDocumentsPerOwner = 5;
    public const int MaxDocuments = 1000;
    public const int MaxTotalBytes = 100 * 1024 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private readonly object gate = new();
    private readonly Dictionary<Guid, StoredDocument> documents = [];
    private readonly TimeProvider clock;
    private readonly ITimer cleanupTimer;
    private int totalBytes;
    private bool disposed;

    /**
     * Creates an isolated temporary store and schedules automatic expiry without a dependency on HTTP or PostgreSQL.
     *
     * @param clock The UTC clock and timer provider; defaults to the system clock.
     */
    public MemoryTicketImportStore(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        cleanupTimer = this.clock.CreateTimer(_ => CleanupExpired(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /**
     * Copies one document after atomically checking the per-owner and total storage bounds.
     *
     * @param issuer The validated token issuer, matched ordinally.
     * @param subject The validated token subject, matched ordinally.
     * @param tripId The public identifier of the owner's trip.
     * @param fileName The API-provided display name.
     * @param content The validated PDF bytes to copy.
     * @return An independent document copy, or null for invalid input or exhausted capacity.
     */
    public TicketImportDocument? Add(string issuer, string subject, Guid tripId, string fileName, ReadOnlyMemory<byte> content)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            ScheduleCleanup(now);
            if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject) || tripId == Guid.Empty ||
                string.IsNullOrWhiteSpace(fileName) || content.Length is < 1 or > MaxFileBytes ||
                documents.Count >= MaxDocuments || content.Length > MaxTotalBytes - totalBytes ||
                documents.Values.Count(document => document.Issuer == issuer && document.Subject == subject) >= MaxDocumentsPerOwner)
                return null;

            var document = new TicketImportDocument(Guid.NewGuid(), tripId, fileName, content.ToArray(), now.Add(Lifetime));
            documents.Add(document.ImportId, new StoredDocument(issuer, subject, document));
            totalBytes += document.Content.Length;
            ScheduleCleanup(now);
            return Copy(document);
        }
    }

    /**
     * Returns a defensive copy only when the exact owner, trip and unexpired identifier match.
     *
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param tripId The public identifier of the owner's trip.
     * @param importId The random temporary document identifier.
     * @return An independent document copy, or null when absent, expired or inaccessible.
     */
    public TicketImportDocument? Get(string issuer, string subject, Guid tripId, Guid importId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            ScheduleCleanup(now);
            return documents.TryGetValue(importId, out var stored) && BelongsTo(stored, issuer, subject, tripId)
                ? Copy(stored.Document)
                : null;
        }
    }

    /**
     * Clears the retained PDF bytes only when the exact owner and trip match.
     *
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param tripId The public identifier of the owner's trip.
     * @param importId The random temporary document identifier.
     * @return True when removed; false when absent, expired or inaccessible.
     */
    public bool Remove(string issuer, string subject, Guid tripId, Guid importId)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            var found = documents.TryGetValue(importId, out var stored) && BelongsTo(stored, issuer, subject, tripId);
            if (found) ClearAndRemove(importId);
            ScheduleCleanup(now);
            return found;
        }
    }

    /** Clears all retained PDF bytes and stops expiry scheduling; repeated disposal is harmless. */
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            cleanupTimer.Dispose();
            foreach (var stored in documents.Values) CryptographicOperations.ZeroMemory(stored.Document.Content);
            documents.Clear();
            totalBytes = 0;
        }
    }

    private void CleanupExpired()
    {
        lock (gate)
        {
            if (disposed) return;
            var now = clock.GetUtcNow();
            RemoveExpired(now);
            ScheduleCleanup(now);
        }
    }

    private void RemoveExpired(DateTimeOffset now)
    {
        foreach (var importId in documents.Where(pair => pair.Value.Document.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            ClearAndRemove(importId);
    }

    private void ClearAndRemove(Guid importId)
    {
        if (!documents.Remove(importId, out var stored)) return;
        totalBytes -= stored.Document.Content.Length;
        CryptographicOperations.ZeroMemory(stored.Document.Content);
    }

    private void ScheduleCleanup(DateTimeOffset now)
    {
        var due = documents.Count == 0
            ? Timeout.InfiniteTimeSpan
            : documents.Values.Min(stored => stored.Document.ExpiresAt) - now;
        cleanupTimer.Change(due, Timeout.InfiniteTimeSpan);
    }

    private static bool BelongsTo(StoredDocument stored, string issuer, string subject, Guid tripId) =>
        stored.Issuer == issuer && stored.Subject == subject && stored.Document.TripId == tripId;

    private static TicketImportDocument Copy(TicketImportDocument document) => document with { Content = document.Content.ToArray() };

    private sealed record StoredDocument(string Issuer, string Subject, TicketImportDocument Document);
}
