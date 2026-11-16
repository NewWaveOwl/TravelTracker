using TravelTracker.Accommodations.Api.Access;
using TravelTracker.Accommodations.Data.Repositories;
using TravelTracker.Accommodations.Domain.Models;

namespace TravelTracker.Accommodations.Api.Services;

/** Applies accommodation ownership, visibility, search and validation rules. */
public sealed class AccommodationService(IAccommodationRepository repository, CallerContext caller) : IAccommodationService
{
    /**
     * Loads only records the authenticated caller may read and applies a literal name filter.
     * @param nameFilter Optional case-insensitive substring.
     * @param cancellationToken Cancels database I/O.
     * @return Visible records ordered by name and then public identifier.
     * @throws UnauthorizedAccessException when the validated identity is missing.
     */
    public async Task<List<Accommodation>> GetAllAsync(string? nameFilter, CancellationToken cancellationToken = default)
    {
        RequireIdentity();
        var records = await repository.GetVisibleAsync(caller.Issuer, caller.Subject, !caller.IsBanned, cancellationToken);
        IEnumerable<Accommodation> filtered = records;
        if (!string.IsNullOrWhiteSpace(nameFilter))
        {
            var term = nameFilter.Trim();
            filtered = filtered.Where(record => record.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        return filtered.OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(record => record.AccommodationId)
            .ToList();
    }

    /**
     * Retrieves one record only through the owner/shared visibility query.
     * @param accommodationId Public identifier.
     * @param cancellationToken Cancels database I/O.
     * @return The visible record, or null if absent or private to another caller.
     * @throws UnauthorizedAccessException when the validated identity is missing.
     */
    public Task<Accommodation?> GetByIdAsync(Guid accommodationId, CancellationToken cancellationToken = default)
    {
        RequireIdentity();
        return repository.GetVisibleByIdAsync(accommodationId, caller.Issuer, caller.Subject,
            !caller.IsBanned, cancellationToken);
    }

    /**
     * Creates a record using server-owned identity and owner data, then validates before persistence.
     * @param accommodation Candidate writable values.
     * @param cancellationToken Cancels database I/O.
     * @return The accepted and saved record.
     * @throws UnauthorizedAccessException when the identity is missing, the caller is banned, or a non-administrator requests shared visibility.
     * @throws AccommodationValidationException when any domain field is invalid.
     */
    public async Task<Accommodation> CreateAsync(Accommodation accommodation, CancellationToken cancellationToken = default)
    {
        if (!HasIdentity || caller.IsBanned || (accommodation.IsShared && !caller.IsAdministrator))
            throw new UnauthorizedAccessException("Caller cannot create this accommodation.");

        accommodation.AccommodationId = Guid.CreateVersion7();
        accommodation.OwnerIssuer = caller.Issuer;
        accommodation.OwnerSubject = caller.Subject;
        accommodation.Name = accommodation.Name.Trim();
        // a null address is left for Validate to report instead of throwing here
        if (accommodation.Address is not null) accommodation.Address = accommodation.Address.Trim();
        var errors = accommodation.Validate();
        if (errors.Count != 0) throw new AccommodationValidationException(errors);

        await repository.AddAsync(accommodation, cancellationToken);
        return accommodation;
    }

    private bool HasIdentity => !string.IsNullOrWhiteSpace(caller.Issuer) && !string.IsNullOrWhiteSpace(caller.Subject);

    /** Fails closed instead of treating a missing identity as an empty catalogue. */
    private void RequireIdentity()
    {
        if (!HasIdentity) throw new UnauthorizedAccessException("A validated caller identity is required.");
    }
}
