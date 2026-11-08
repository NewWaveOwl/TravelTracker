using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Data.Repositories;

/** Defines durable accommodation operations with visibility scoped to a validated caller. */
public interface IAccommodationRepository
{
    /**
     * Loads only shared items allowed by the caller and the caller's own private records.
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param includeShared Whether the caller may view shared catalogue entries.
     * @param cancellationToken Cancels database I/O.
     * @return Detached visible domain records.
     */
    Task<List<Accommodation>> GetVisibleAsync(string issuer, string subject, bool includeShared,
        CancellationToken cancellationToken = default);

    /**
     * Loads a record only when it is visible to the validated caller.
     * @param accommodationId Public accommodation identifier.
     * @param issuer The validated token issuer.
     * @param subject The validated token subject.
     * @param includeShared Whether the caller may view shared catalogue entries.
     * @param cancellationToken Cancels database I/O.
     * @return The detached record, or null if missing or private to another caller.
     */
    Task<Accommodation?> GetVisibleByIdAsync(Guid accommodationId, string issuer, string subject, bool includeShared,
        CancellationToken cancellationToken = default);

    /**
     * Persists an accepted accommodation record.
     * @param accommodation The validated record with server-owned identity and ownership.
     * @param cancellationToken Cancels database I/O.
     * @return Completion after the row is saved.
     */
    Task AddAsync(Accommodation accommodation, CancellationToken cancellationToken = default);
}
