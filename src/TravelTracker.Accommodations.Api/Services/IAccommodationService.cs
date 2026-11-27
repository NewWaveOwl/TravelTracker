using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Api.Services;

/** Defines the authenticated accommodation catalogue operations. */
public interface IAccommodationService
{
    /**
     * Publishes an owned place or acknowledges a previously shared place.
     * @param id Existing public identifier.
     * @param cancellationToken Cancels storage work.
     * @return Whether the place is now shared.
     */
    Task<bool> ShareAsync(Guid id, CancellationToken cancellationToken);

    /**
     * Lists visible accommodations, optionally filtered by a name substring.
     * @param nameFilter Optional literal case-insensitive name substring.
     * @param cancellationToken Cancels persistence I/O.
     * @return Visible matches in deterministic order.
     */
    Task<List<Accommodation>> GetAllAsync(string? nameFilter, CancellationToken cancellationToken = default);

    /**
     * Loads a single accommodation only when the caller may see it.
     * @param accommodationId Public accommodation identifier.
     * @param cancellationToken Cancels persistence I/O.
     * @return The visible record or null when absent/private to another caller.
     */
    Task<Accommodation?> GetByIdAsync(Guid accommodationId, CancellationToken cancellationToken = default);

    /**
     * Validates and persists a caller-owned accommodation.
     * @param accommodation Writable candidate values.
     * @param cancellationToken Cancels persistence I/O.
     * @return The saved record with server-assigned identity and ownership.
     */
    Task<Accommodation> CreateAsync(Accommodation accommodation, CancellationToken cancellationToken = default);
}
